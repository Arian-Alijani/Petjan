namespace Animora.Application.Auditing;

/// <summary>Registration-time audit metadata carried by a frozen pipeline entry.</summary>
public sealed record AuditBinding(AuditCategory Category, string Action);
