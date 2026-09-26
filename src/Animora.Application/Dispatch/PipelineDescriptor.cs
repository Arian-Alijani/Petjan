namespace Animora.Application.Dispatch;

/// <summary>
/// Registration-time facts a behavior may use to specialize (or skip) its link. <see cref="PermissionCode"/> is the
/// request's declared catalog permission (B04a), <c>null</c> when unannotated (authenticated-only).
/// </summary>
public sealed record PipelineDescriptor(Type RequestType, Type ResultType, RequestKind Kind, bool HasValidators,
    string? PermissionCode = null);
