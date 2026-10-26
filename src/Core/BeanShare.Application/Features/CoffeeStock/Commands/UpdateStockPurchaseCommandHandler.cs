using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.CoffeeStock.Dtos;
using BeanShare.Domain.Common;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.CoffeeStock.Commands;

public sealed class UpdateStockPurchaseCommandHandler
    : IRequestHandler<UpdateStockPurchaseCommand, Result<StockPurchaseDto>>
{
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserContext _userContext;
    private readonly IClock _clock;
    private readonly IUserRepository _userRepository;

    public UpdateStockPurchaseCommandHandler(
        ICoffeeStockRepository coffeeStockRepository,
        ISpaceRepository spaceRepository,
        IUserContext userContext,
        IClock clock,
        IUserRepository userRepository)
    {
        _userRepository = userRepository;
        _coffeeStockRepository = coffeeStockRepository;
        _spaceRepository = spaceRepository;
        _userContext = userContext;
        _clock = clock;
    }

    public async Task<Result<StockPurchaseDto>> Handle(
        UpdateStockPurchaseCommand command,
        CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.CurrentUserId;
        var spaceId = new SpaceId(command.SpaceId);

        var spaceSpec = new SpaceByIdSpecification(spaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(spaceSpec, cancellationToken);

        if (space == null)
            return Result<StockPurchaseDto>.Failure(Error.SpaceNotFound(command.SpaceId));

        if (!space.IsAdmin(currentUserId))
            return Result<StockPurchaseDto>.Failure(Error.InsufficientSpacePrivileges("edit stock purchases"));

        UserId? paidBy = command.PaidByUserId is { } payerId ? new UserId(payerId) : null;
        if (paidBy is { } payer && !space.HasMember(payer))
            return Result<StockPurchaseDto>.Failure(Error.MemberNotFound(payer.Value, command.SpaceId));
        if (paidBy is { } mergedCandidate && await _userRepository.IsMergedAsync(mergedCandidate, cancellationToken))
            return Result<StockPurchaseDto>.Failure(Error.MemberMerged());

        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        if (coffeeStock == null)
            return Result<StockPurchaseDto>.Failure(Error.StockNotFound(command.SpaceId));

        // A "Paid in Excel" bag must stay before the ledger start (H4), and a date change is a ledger input,
        // so it shares the space token with UpdateLedgerSettings (I4).
        var existing = coffeeStock.Purchases.FirstOrDefault(p => p.Id == command.PurchaseId);
        if (existing is { ExcludedFromLedgerCredit: true } && space.LedgerStartDate is { } ledgerStart && command.PurchasedAt >= ledgerStart)
            return Result<StockPurchaseDto>.Failure(Error.BagPaidInExcel(BagNames.Of(existing)));
        var dateChanged = existing is not null && existing.PurchasedAt != command.PurchasedAt;

        try
        {
            CoffeeType? coffeeType = null;
            if (!string.IsNullOrEmpty(command.CoffeeType) && Enum.TryParse<CoffeeType>(command.CoffeeType, out var parsedType))
                coffeeType = parsedType;

            coffeeStock.UpdatePurchase(
                command.PurchaseId,
                Weight.FromGrams(command.QuantityGrams),
                command.CostAmount,
                command.PurchasedAt,
                _clock,
                command.ProductName,
                command.ProductBrand,
                coffeeType);

            if (paidBy.HasValue)
                coffeeStock.SetPurchasePaidBy(command.PurchaseId, paidBy, _clock);

            await _coffeeStockRepository.UpdateAsync(coffeeStock, cancellationToken);

            if (dateChanged)
            {
                space.BumpLedgerVersion();
                await _spaceRepository.UpdateAsync(space, cancellationToken);
            }

            var purchase = coffeeStock.Purchases.First(p => p.Id == command.PurchaseId);
            var dto = new StockPurchaseDto
            {
                Id = purchase.Id,
                ProductName = purchase.Product.Name,
                ProductBrand = purchase.Product.Brand,
                ProductType = purchase.Product.Type.ToString(),
                QuantityGrams = purchase.Quantity.Grams,
                CostAmount = purchase.Cost.Amount,
                CostCurrency = purchase.Cost.Currency,
                Vendor = purchase.Vendor,
                PurchasedBy = purchase.PurchasedBy.Value,
                PaidByUserId = purchase.PaidByUserId?.Value,
                PurchasedAt = purchase.PurchasedAt,
                CreatedAt = purchase.CreatedAt,
                CostPerGram = purchase.CostPerGram.Amount,
                ExcludedFromLedgerCredit = purchase.ExcludedFromLedgerCredit
            };

            return Result<StockPurchaseDto>.Success(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Result<StockPurchaseDto>.Failure(Error.DomainError(ex.Message));
        }
        catch (ArgumentException ex)
        {
            return Result<StockPurchaseDto>.Failure(Error.ValidationFailure(nameof(UpdateStockPurchaseCommand), ex.Message));
        }
    }
}
