using Animora.Domain.Primitives;

namespace Animora.Application.Correlation;

/// <summary>The single client-side source of <c>OperationId</c>s (A-RULE 005).</summary>
public sealed class OperationIdFactory
{
#pragma warning disable CA1822 // Instance member by design: injected, so tests and later phases can substitute the source.
    public string New() => IdGenerator.NewId();
#pragma warning restore CA1822
}
