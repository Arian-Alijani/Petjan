using Animora.Application.Auditing;

namespace Animora.Application.Dispatch;

/// <summary>
/// A frozen registry entry. <see cref="Pipeline"/> is the typed <see cref="PipelineStep{TResult}"/> the
/// dispatcher calls (no boxing); <see cref="Invoker"/> is the same chain behind an untyped façade for
/// callers that only know the request type at run time (e.g. replay of stored commands, B02).
/// Both are closures compiled once at registration — no <c>MethodInfo.Invoke</c>, no activation at call time.
/// <see cref="PermissionCode"/> is the catalog code declared by <c>RequiresPermissionAttribute</c>, read once at
/// registration (B04a); <c>null</c> means authenticated-only. <see cref="Audit"/> is likewise resolved once (B05a).
/// </summary>
public sealed record HandlerEntry(
    Type RequestType,
    Type ResultType,
    Type HandlerType,
    RequestKind Kind,
    Delegate Pipeline,
    Func<IServiceProvider, object, object, CancellationToken, Task<object>> Invoker,
    string? PermissionCode = null,
    AuditBinding? Audit = null);
