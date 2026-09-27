namespace Animora.Application.Auditing;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class AuditableAttribute(AuditCategory category, string action) : Attribute
{
    public AuditCategory Category { get; } = category;

    public string Action { get; } = string.IsNullOrWhiteSpace(action)
        ? throw new ArgumentException("Audit action is required.", nameof(action))
        : action;
}
