using Animora.Domain.Results;

namespace Animora.Application.Dispatch;

/// <summary>
/// One pre-composed link of a handler's behavior chain. <paramref name="context"/> is a
/// <c>CommandContext</c> or <c>QueryContext</c> matching the entry's <see cref="RequestKind"/>.
/// </summary>
public delegate Task<Result<TResult>> PipelineStep<TResult>(
    IServiceProvider services, object request, object context, CancellationToken ct);
