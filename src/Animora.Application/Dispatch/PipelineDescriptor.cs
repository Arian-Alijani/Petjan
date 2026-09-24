namespace Animora.Application.Dispatch;

/// <summary>Registration-time facts a behavior may use to specialize (or skip) its link.</summary>
public sealed record PipelineDescriptor(Type RequestType, Type ResultType, RequestKind Kind, bool HasValidators);
