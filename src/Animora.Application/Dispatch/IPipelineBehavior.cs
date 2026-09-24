namespace Animora.Application.Dispatch;

/// <summary>
/// One fixed stage of the dispatch chain. <see cref="Wrap{TRequest,TResult}"/> runs once per handler entry
/// at registration; the returned step runs per call.
/// </summary>
public interface IPipelineBehavior
{
    /// <summary>Stable stage name (diagnostics/tests).</summary>
    string Name { get; }

    PipelineStep<TResult> Wrap<TRequest, TResult>(PipelineStep<TResult> next, PipelineDescriptor descriptor)
        where TRequest : notnull;
}
