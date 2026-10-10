namespace Animora.Persistence.PostgreSql.Outbox;

/// <summary>Trusted scoped switch set by server workers before any context is resolved.</summary>
public sealed class WorkerExecutionContext
{
    public bool IsWorker { get; set; }
}
