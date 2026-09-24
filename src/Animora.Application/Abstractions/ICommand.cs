namespace Animora.Application.Abstractions;

/// <summary>
/// Marker for a state-changing use case. <typeparamref name="TResult"/> is the payload carried inside
/// <c>Result&lt;TResult&gt;</c>. Commands are <c>sealed record</c>s named <c>&lt;Verb&gt;&lt;Aggregate&gt;Command</c>.
/// </summary>
/// <typeparam name="TResult">The success payload type.</typeparam>
#pragma warning disable CA1040 // Marker interface by design: carries TResult for compile-time dispatch typing.
public interface ICommand<TResult>;
#pragma warning restore CA1040
