namespace Animora.Domain.Results;

/// <summary>
/// Outcome of an operation that returns no value. Expected failures are modelled as an
/// <see cref="ErrorCode"/>, never as an exception (CONVENTIONS.md §4).
/// </summary>
public readonly record struct Result
{
    private Result(bool isSuccess, ErrorCode error, string? detail)
    {
        IsSuccess = isSuccess;
        Error = error;
        Detail = detail;
    }

    /// <summary><see langword="true"/> when the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary><see langword="true"/> when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The failure code; <see cref="ErrorCode.None"/> on success.</summary>
    public ErrorCode Error { get; }

    /// <summary>
    /// Diagnostic context for logs and support — an invariant-culture developer string.
    /// Never a user-facing localized message: the Desktop maps <see cref="Error"/> to Persian text.
    /// </summary>
    public string? Detail { get; }

    /// <summary>A successful result.</summary>
    public static Result Ok() => new(true, ErrorCode.None, null);

    /// <summary>A failed result carrying <paramref name="error"/>.</summary>
    public static Result Fail(ErrorCode error, string? detail = null) => new(false, error, detail);

    /// <summary>Runs <paramref name="next"/> only when this result is successful.</summary>
    public Result Bind(Func<Result> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess ? next() : this;
    }

    /// <summary>Runs <paramref name="next"/> only when this result is successful.</summary>
    public Result<TOut> Bind<TOut>(Func<Result<TOut>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess ? next() : Result<TOut>.Fail(Error, Detail);
    }

    /// <inheritdoc />
    public override string ToString() => IsSuccess ? "Ok" : $"Fail({Error})";
}

/// <summary>
/// Outcome of an operation that returns a <typeparamref name="T"/> on success.
/// </summary>
/// <typeparam name="T">The success payload type.</typeparam>
public readonly record struct Result<T>
{
    private Result(bool isSuccess, T? value, ErrorCode error, string? detail)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        Detail = detail;
    }

    /// <summary><see langword="true"/> when the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary><see langword="true"/> when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The payload; <see langword="default"/> when <see cref="IsFailure"/>.</summary>
    public T? Value { get; }

    /// <summary>The failure code; <see cref="ErrorCode.None"/> on success.</summary>
    public ErrorCode Error { get; }

    /// <summary>
    /// Diagnostic context for logs and support — an invariant-culture developer string.
    /// Never a user-facing localized message.
    /// </summary>
    public string? Detail { get; }

    /// <summary>A successful result carrying <paramref name="value"/>.</summary>
    public static Result<T> Ok(T value) => new(true, value, ErrorCode.None, null);

    /// <summary>A failed result carrying <paramref name="error"/>.</summary>
    public static Result<T> Fail(ErrorCode error, string? detail = null) => new(false, default, error, detail);

    /// <summary>Lifts a value into a successful result.</summary>
    public static implicit operator Result<T>(T value) => Ok(value);

    /// <summary>Named alternative to the implicit conversion (CA2225).</summary>
    public static Result<T> FromValue(T value) => Ok(value);

    /// <summary>Drops the payload, keeping success/failure and the error.</summary>
    public Result ToResult() => IsSuccess ? Result.Ok() : Result.Fail(Error, Detail);

    /// <summary>Transforms the payload when successful; propagates the error otherwise.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return IsSuccess
            ? Result<TOut>.Ok(map(Value!)) // Value is non-null by construction when IsSuccess.
            : Result<TOut>.Fail(Error, Detail);
    }

    /// <summary>Chains another fallible step when successful; propagates the error otherwise.</summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess
            ? next(Value!) // Value is non-null by construction when IsSuccess.
            : Result<TOut>.Fail(Error, Detail);
    }

    /// <summary>Chains a value-less step when successful; propagates the error otherwise.</summary>
    public Result Bind(Func<T, Result> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess
            ? next(Value!) // Value is non-null by construction when IsSuccess.
            : Result.Fail(Error, Detail);
    }

    /// <summary>Returns the payload when successful, otherwise <paramref name="fallback"/>.</summary>
    public T OrElse(T fallback) => IsSuccess ? Value! : fallback;

    /// <inheritdoc />
    public override string ToString() => IsSuccess ? $"Ok({Value})" : $"Fail({Error})";
}
