namespace BeanShare.Application.Features.Balances.Dtos;

/// <summary>How many existing, not yet billed cups a candidate ledger start date would leave out of money.</summary>
public sealed record LedgerStartImpactDto(DateTime LedgerStartDate, int CupsBeforeStart);
