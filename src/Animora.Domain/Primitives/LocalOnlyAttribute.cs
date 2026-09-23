namespace Animora.Domain.Primitives;

/// <summary>
/// Marks an entity type as device-local: it is never included in any replication
/// scope. Consumed by the model builder in Phase A09 and by the sync scope in
/// Phase E03; it carries no behavior of its own.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class LocalOnlyAttribute : Attribute
{
}
