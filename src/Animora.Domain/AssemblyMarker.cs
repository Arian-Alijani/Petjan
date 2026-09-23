namespace Animora.Domain;

/// <summary>
/// Anchor type for <c>Animora.Domain</c>. Exists so tests and composition roots
/// can reference the assembly via <c>typeof(AssemblyMarker)</c> before any real
/// domain type exists. Real entities/value objects arrive from Phase A02 onward.
/// </summary>
public static class AssemblyMarker
{
}
