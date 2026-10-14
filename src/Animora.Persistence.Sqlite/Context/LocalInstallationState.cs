using Animora.Persistence.Sqlite.Entities;

namespace Animora.Persistence.Sqlite.Context;

/// <summary>Singleton cache of the bootstrapped <see cref="LocalInstallation"/> row (no DB access).</summary>
public sealed class LocalInstallationState
{
    /// <summary>Tenant used before bootstrap; seeding and the Tenant filter keep matching it.</summary>
    public const string DefaultTenantId = "T-LOCAL-DEFAULT";

    // Serializes a tenant transition with state pulls and command settlement passes.
    public SemaphoreSlim TransitionGate { get; } = new(1, 1);

    private volatile LocalInstallation? _installation;

    public LocalInstallation? Installation => _installation;

    public string TenantId => _installation?.TenantId ?? DefaultTenantId;

    internal void Set(LocalInstallation installation) => _installation = installation;
}
