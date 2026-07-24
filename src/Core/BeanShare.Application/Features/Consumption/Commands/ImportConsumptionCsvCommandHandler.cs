using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Application.Features.Consumption.Dtos;
using BeanShare.Domain.Common;
using BeanShare.Domain.Entities;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Consumption.Commands;

public sealed class ImportConsumptionCsvCommandHandler
    : IRequestHandler<ImportConsumptionCsvCommand, Result<ImportConsumptionCsvResult>>
{
    private readonly ICoffeeStockRepository _coffeeStockRepository;
    private readonly IConsumptionRepository _consumptionRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClock _clock;

    public ImportConsumptionCsvCommandHandler(
        ICoffeeStockRepository coffeeStockRepository,
        IConsumptionRepository consumptionRepository,
        ISpaceRepository spaceRepository,
        IUserRepository userRepository,
        IClock clock)
    {
        _coffeeStockRepository = coffeeStockRepository;
        _consumptionRepository = consumptionRepository;
        _spaceRepository = spaceRepository;
        _userRepository = userRepository;
        _clock = clock;
    }

    public async Task<Result<ImportConsumptionCsvResult>> Handle(
        ImportConsumptionCsvCommand request,
        CancellationToken cancellationToken)
    {
        var spaceId = new SpaceId(request.SpaceId);
        var spec = new SpaceByIdSpecification(spaceId);
        var space = await _spaceRepository.GetSingleBySpecAsync(spec, cancellationToken);

        if (space == null)
            return Result<ImportConsumptionCsvResult>.Failure(Error.SpaceNotFound(request.SpaceId));

        var uniqueEmails = request.Rows.Select(r => r.Email.Trim().ToLowerInvariant()).Distinct().ToList();
        var emailToUser = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);

        foreach (var email in uniqueEmails)
        {
            var user = await _userRepository.GetByEmailAsync(email, cancellationToken);
            if (user != null)
                emailToUser[email] = user;
        }

        var errors = new List<ImportRowError>();
        var entriesToCreate = new List<ConsumptionEntry>();

        foreach (var row in request.Rows)
        {
            var rowErrors = ValidateRow(row, emailToUser, space);
            if (rowErrors.Count > 0)
            {
                errors.AddRange(rowErrors);
                continue;
            }

            var user = emailToUser[row.Email.Trim()];
            var coffeeType = Enum.Parse<CoffeeType>(row.ProductType, ignoreCase: true);
            var product = CoffeeProduct.Create(row.ProductName.Trim(), row.ProductBrand.Trim(), coffeeType);
            var quantity = Weight.FromGrams(row.QuantityGrams);

            var entry = ConsumptionEntry.Create(
                spaceId,
                user.Id,
                product,
                quantity,
                row.ConsumedAt,
                _clock);

            entriesToCreate.Add(entry);
        }

        foreach (var entry in entriesToCreate)
        {
            await _consumptionRepository.AddAsync(entry, cancellationToken);
        }

        var warnings = new List<string>();
        var coffeeStock = await _coffeeStockRepository.GetBySpaceIdAsync(spaceId, cancellationToken);
        if (coffeeStock != null)
        {
            var failedProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entriesToCreate)
            {
                var productKey = $"{entry.Product.Name}|{entry.Product.Brand}|{entry.Product.Type}";
                if (failedProducts.Contains(productKey))
                    continue;

                try
                {
                    var allocation = coffeeStock.ConsumeStock(entry.Product, entry.Quantity, _clock);
                    entry.SetAllocation(allocation.LotId, allocation.Cost, allocation.UnitCostPerGram);
                }
                catch (ProductNotFoundException)
                {
                    failedProducts.Add(productKey);
                    warnings.Add($"Product '{entry.Product.Name}' by '{entry.Product.Brand}' ({entry.Product.Type}) not found in stock. Consumption entries were imported but stock was not deducted.");
                }
                catch (InsufficientStockException)
                {
                    warnings.Add($"Insufficient stock for '{entry.Product.Name}' by '{entry.Product.Brand}'. Remaining entries for this product were not deducted from stock.");
                    failedProducts.Add(productKey);
                }
            }

            await _coffeeStockRepository.UpdateAsync(coffeeStock, cancellationToken);
        }
        else
        {
            warnings.Add("No stock record found for this space. Consumption entries were imported but stock was not deducted.");
        }

        var result = new ImportConsumptionCsvResult
        {
            TotalRows = request.Rows.Count,
            SuccessCount = entriesToCreate.Count,
            ErrorCount = errors.Count,
            Warnings = warnings,
            Errors = errors
        };

        return Result<ImportConsumptionCsvResult>.Success(result);
    }

    private List<ImportRowError> ValidateRow(
        ImportConsumptionCsvRow row,
        Dictionary<string, User> emailToUser,
        Domain.Aggregates.Space.Space space)
    {
        var errors = new List<ImportRowError>();

        if (string.IsNullOrWhiteSpace(row.Email))
        {
            errors.Add(new ImportRowError(row.RowNumber, "Email", "Email is required"));
            return errors;
        }

        if (!emailToUser.TryGetValue(row.Email.Trim(), out var user))
        {
            errors.Add(new ImportRowError(row.RowNumber, "Email", $"No user found with email '{row.Email.Trim()}'"));
            return errors;
        }

        if (!space.HasMember(user.Id))
        {
            errors.Add(new ImportRowError(row.RowNumber, "Email", $"User '{row.Email.Trim()}' is not a member of this space"));
            return errors;
        }

        if (string.IsNullOrWhiteSpace(row.ProductName))
            errors.Add(new ImportRowError(row.RowNumber, "ProductName", "Product name is required"));
        else if (row.ProductName.Length > 200)
            errors.Add(new ImportRowError(row.RowNumber, "ProductName", "Product name cannot exceed 200 characters"));

        if (string.IsNullOrWhiteSpace(row.ProductBrand))
            errors.Add(new ImportRowError(row.RowNumber, "ProductBrand", "Product brand is required"));
        else if (row.ProductBrand.Length > 200)
            errors.Add(new ImportRowError(row.RowNumber, "ProductBrand", "Product brand cannot exceed 200 characters"));

        if (!Enum.TryParse<CoffeeType>(row.ProductType, ignoreCase: true, out _))
            errors.Add(new ImportRowError(row.RowNumber, "ProductType", "Must be one of: Espresso, Filter, Instant, Decaf, Specialty"));

        if (row.QuantityGrams <= 0)
            errors.Add(new ImportRowError(row.RowNumber, "QuantityGrams", "Must be greater than 0"));

        if (row.ConsumedAt > _clock.UtcNow.AddMinutes(5))
            errors.Add(new ImportRowError(row.RowNumber, "ConsumedAt", "Date cannot be in the future"));

        return errors;
    }
}
