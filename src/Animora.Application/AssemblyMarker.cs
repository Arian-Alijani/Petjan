namespace Animora.Application;

/// <summary>
/// Anchor type for <c>Animora.Application</c>. Exists so tests and composition
/// roots can reference the assembly via <c>typeof(AssemblyMarker)</c> before any
/// real use case exists. Real handlers/ports arrive from Phase A02 onward.
/// </summary>
public static class AssemblyMarker
{
}
