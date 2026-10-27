namespace BeanShare.Application.Common;

/// <summary>
/// Marks a command that is also retried when it loses a race on the space's ledger token (alone or with bag
/// counters): a new cup, which cannot conflict on a row of its own. Commands that change an existing cup are not
/// marked, because a conflict reported on the token may hide one on the cup itself.
/// </summary>
public interface IRetryOnLedgerTokenConflict : IRetryOnConcurrencyConflict
{
}
