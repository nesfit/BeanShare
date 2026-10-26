using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;
using BeanShare.Domain.Common;
using BeanShare.Domain.Enums;
using BeanShare.Domain.Exceptions;
using BeanShare.Domain.Specifications;
using BeanShare.Domain.ValueObjects;
using MediatR;

namespace BeanShare.Application.Features.Settlement.Commands.ConfirmPayment;

/// <summary>
/// Confirms a member's line: on a ledger settlement the member, the settlement's treasurer or a space
/// admin may confirm; the treasurer's own line resolves itself, and a carried line is confirmed in the
/// settlement it was carried into. Legacy settlements keep the member-or-admin rule.
/// </summary>
public sealed class ConfirmPaymentHandler(
    ISettlementRepository settlementRepository,
    ISpaceRepository spaceRepository,
    IUserContext userContext,
    IClock clock) : IRequestHandler<ConfirmPaymentCommand, Result>
{
    public async Task<Result> Handle(ConfirmPaymentCommand command, CancellationToken cancellationToken)
    {
        var settlement = await settlementRepository.GetByIdAsync(
            new SettlementId(command.SettlementId), cancellationToken);

        if (settlement is null)
            return Result.Failure(Error.SettlementNotFound(command.SettlementId));

        var space = await spaceRepository.GetSingleBySpecAsync(
            new SpaceByIdSpecification(settlement.SpaceId), cancellationToken);

        if (space is null)
            return Result.Failure(Error.SpaceNotFound(settlement.SpaceId.Value));

        if (!CanConfirmPayment(space, settlement, command.MemberUserId))
            return Result.Failure(Error.InsufficientSpacePrivileges("confirm payments for other members"));

        var memberUserId = new UserId(command.MemberUserId);
        var line = settlement.GetLineForUser(memberUserId);

        if (line is null)
            return Result.Failure(Error.SettlementLineNotFound(command.MemberUserId));
        if (line.IsCarried)
            return Result.Failure(new Error(Error.Codes.LineCarried,
                $"This line was carried into settlement {line.CarriedToSettlementId}; confirm it there"));
        if (line.IsConfirmed)
            return Result.Failure(Error.PaymentAlreadyConfirmed(command.MemberUserId));
        if (settlement.Status is SettlementStatus.Completed or SettlementStatus.Generated)
            return Result.Failure(Error.InvalidSettlementState("confirm payment", settlement.Status.ToString()));
        if (settlement.IsTreasurerLine(line))
            return Result.Failure(Error.ConfirmationFailed("The treasurer's line resolves itself once every other line is confirmed"));

        try
        {
            settlement.ConfirmPayment(memberUserId, userContext.CurrentUserId, clock);
            await settlementRepository.UpdateAsync(settlement, cancellationToken);
            return Result.Success();
        }
        catch (LedgerDomainException ex)
        {
            return Result.Failure(new Error(ex.Code, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.ConfirmationFailed(ex.Message));
        }
    }

    private bool CanConfirmPayment(Domain.Aggregates.Space.Space space, Domain.Aggregates.Settlement.Settlement settlement, Guid targetMemberUserId)
    {
        var currentUserId = userContext.CurrentUserId;
        return space.IsAdmin(currentUserId)
            || currentUserId.Value == targetMemberUserId
            // The settlement's treasurer keeps the right after a treasurer change, but only while a member.
            || (settlement.IsLedger && settlement.TreasurerUserId == currentUserId && space.HasMember(currentUserId));
    }
}
