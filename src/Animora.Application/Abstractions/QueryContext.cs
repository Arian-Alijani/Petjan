namespace Animora.Application.Abstractions;

/// <summary>The only carrier of ambient identity into a query handler.</summary>
public sealed record QueryContext(string TenantId, string UserId, string CorrelationId);
