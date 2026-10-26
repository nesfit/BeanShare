namespace BeanShare.Application.Common;

/// <summary>
/// The one cap on pasted text (balance entries and history), shared by the dialogs and the handlers (I10).
/// A text that reaches it may have been cut by the textarea, so it is refused rather than read short.
/// </summary>
public static class PasteLimits
{
    public const int MaxLength = 250_000;

    public static Error? CheckLength(string? text, string field) =>
        text is { Length: >= MaxLength }
            ? Error.ValidationFailure(field, "The text reached the limit and may be cut; paste fewer rows")
            : null;
}
