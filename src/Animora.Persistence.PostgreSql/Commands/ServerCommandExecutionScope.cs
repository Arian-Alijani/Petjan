namespace Animora.Persistence.PostgreSql.Commands;

/// <summary>Request-scoped identity of the running command; populated by the server dispatcher.</summary>
public sealed class ServerCommandExecutionScope
{
    public string? OperationId { get; set; }
}
