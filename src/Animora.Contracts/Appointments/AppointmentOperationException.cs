namespace Animora.Contracts.Appointments;

/// <summary>
/// A rejected appointment read/write that keeps the backend's structured failure (A10): the stable
/// <see cref="Code"/> (SCREAMING_SNAKE, also the exception <c>Message</c> so older <c>catch</c> sites keep
/// working), the <see cref="Category"/> and the developer-only <see cref="Detail"/>. The Desktop maps the code to
/// localized text; nothing below it produces user-facing strings.
/// </summary>
public sealed class AppointmentOperationException : InvalidOperationException
{
    public AppointmentOperationException(string code, string category, string? detail = null, bool writeCommitted = false)
        : base(code)
    {
        Code = code;
        Category = category;
        Detail = detail;
        WriteCommitted = writeCommitted;
    }

    /// <summary>Stable failure identifier.</summary>
    public string Code { get; }

    /// <summary>Failure category (Validation, NotFound, Conflict, Forbidden, ...).</summary>
    public string Category { get; }

    /// <summary>Diagnostic context for logs/support; never shown to the user.</summary>
    public string? Detail { get; }

    /// <summary>
    /// True when the command itself committed and only a follow-up read failed, so the caller must report a
    /// successful write and must not offer a retry of the write.
    /// </summary>
    public bool WriteCommitted { get; }
}
