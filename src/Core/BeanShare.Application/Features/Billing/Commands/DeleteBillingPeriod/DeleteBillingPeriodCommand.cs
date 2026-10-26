using BeanShare.Application.Abstractions;
using BeanShare.Application.Common;

namespace BeanShare.Application.Features.Billing.Commands.DeleteBillingPeriod;

/// <summary>Deletes a draft billing period (anything else is PERIOD_NOT_DELETABLE). Admin only.</summary>
public sealed record DeleteBillingPeriodCommand(Guid BillingPeriodId) : ICommand<Result>;
