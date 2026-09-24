namespace Animora.Application.Abstractions;

/// <summary>
/// Marker for a read-only use case. Queries are <c>sealed record</c>s named <c>&lt;Subject&gt;Query</c>
/// and never open a transaction.
/// </summary>
/// <typeparam name="TResult">The success payload type.</typeparam>
#pragma warning disable CA1040 // Marker interface by design: carries TResult for compile-time dispatch typing.
public interface IQuery<TResult>;
#pragma warning restore CA1040
