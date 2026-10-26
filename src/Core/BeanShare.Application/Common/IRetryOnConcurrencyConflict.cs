namespace BeanShare.Application.Common;

/// <summary>
/// Marks a command that is retried (in a fresh change tracker) when it loses a race on a bag's
/// stock counters only. Conflicts on anything else still fail with CONCURRENCY_CONFLICT.
/// </summary>
public interface IRetryOnConcurrencyConflict
{
}
