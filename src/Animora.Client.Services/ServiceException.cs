using Animora.Domain.Results;

namespace Animora.Client.Services;

/// <summary>
/// Thrown by the Client.Services seam when a backend <see cref="Result"/> fails. It IS an
/// <see cref="InvalidOperationException"/> whose <see cref="Exception.Message"/> is still the bare error code, so every
/// existing <c>catch (InvalidOperationException ex) when (IsAccessFailure(ex.Message))</c> keeps working, while new
/// code can branch on the machine-readable <see cref="Category"/> (see <see cref="ErrorCode"/>) instead of text.
/// </summary>
public sealed class ServiceException : InvalidOperationException
{
    public ServiceException(ErrorCode error)
        : base(error.Code)
    {
        Code = error.Code;
        Category = error.Category;
    }

    /// <summary>SCREAMING_SNAKE backend error code (identical to <see cref="Exception.Message"/>).</summary>
    public string Code { get; }

    /// <summary>One of the <see cref="ErrorCode"/> category constants (Validation, NotFound, Conflict, …).</summary>
    public string Category { get; }
}
