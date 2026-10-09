namespace Animora.Persistence.PostgreSql.Commands;

/// <summary>Lifecycle of a <see cref="CommandIntakeRecord"/> (stored as <c>smallint</c>).</summary>
public enum IntakeStatus : short
{
    /// <summary>Accepted into intake, not yet leased for execution.</summary>
    Received = 0,

    /// <summary>Leased and executing (or crashed mid-execution when the lease has expired).</summary>
    InProgress = 1,

    /// <summary>Terminal: an outcome is stored. Settled ⇔ the effect (if any) is committed.</summary>
    Settled = 2,

    /// <summary>Execution abandoned after repeated expired leases; a new attempt may re-lease it.</summary>
    Failed = 3,
}
