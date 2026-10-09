using Animora.Domain.Primitives;

namespace Animora.Persistence.PostgreSql.Scheduling;

/// <summary>Tenant-scoped durable replacement for the client's local preferences file.</summary>
public sealed class AppointmentSettingsEntity : SyncableEntity
{
    public string SettingsJson { get; set; } = "{}";
}
