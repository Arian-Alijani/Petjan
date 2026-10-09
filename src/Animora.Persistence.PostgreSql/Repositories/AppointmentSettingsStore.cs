using System.Text.Json;
using Animora.Application.Ports;
using Animora.Contracts.Appointments;
using Animora.Domain.Primitives;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

/// <summary>Tenant-scoped preferences, persisted by self-transacted (non-wire) settings use cases.</summary>
public sealed class AppointmentSettingsStore(AnimoraServerDbContext db, IClock clock) : IAppointmentSettingsStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Colors = ["#3B82F6", "#8B5CF6", "#10B981", "#F59E0B", "#EC4899"];
    public async Task<AppointmentSettings> GetAsync(CancellationToken ct)
    {
        var json = await db.Set<AppointmentSettingsEntity>().AsNoTracking().Select(s => s.SettingsJson)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return json is null ? AppointmentSettings.Default : JsonSerializer.Deserialize<AppointmentSettings>(json, Json)
            ?? throw new InvalidOperationException("Persisted appointment settings are invalid.");
    }
    public Task SaveAsync(AppointmentSettings settings, CancellationToken ct) => MutateAsync(_ => settings, ct);
    public async Task<CustomAppointmentType> AddTypeAsync(string label, int durationMinutes, bool requiresDoctor, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationMinutes);
        CustomAppointmentType? added = null;
        await MutateAsync(settings =>
        {
            added = new CustomAppointmentType(IdGenerator.NewId(clock.UtcNow), label.Trim(),
                Colors[settings.CustomTypes.Count % Colors.Length], durationMinutes, requiresDoctor);
            return settings with { CustomTypes = [.. settings.CustomTypes, added] };
        }, ct).ConfigureAwait(false);
        return added ?? throw new InvalidOperationException("Appointment type was not persisted.");
    }
    public Task RemoveTypeAsync(string id, CancellationToken ct) =>
        MutateAsync(s => s with { CustomTypes = s.CustomTypes.Where(t => t.Id != id).ToArray() }, ct);
    public Task SetCalendarModeAsync(CalendarMode mode, CancellationToken ct) => MutateAsync(s => s with { CalendarMode = mode }, ct);
    public Task SetSmsAsync(AppointmentSmsSettings sms, CancellationToken ct) => MutateAsync(s => s with { Sms = sms }, ct);

    private async Task MutateAsync(Func<AppointmentSettings, AppointmentSettings> mutate, CancellationToken ct)
    {
        // A settings command is self-transacted: no client local file and no save that escapes a command transaction.
        if (db.Database.CurrentTransaction is not null) { await WriteAsync(mutate, ct).ConfigureAwait(false); return; }
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await WriteAsync(mutate, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
    private async Task WriteAsync(Func<AppointmentSettings, AppointmentSettings> mutate, CancellationToken ct)
    {
        var row = await db.Set<AppointmentSettingsEntity>().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (row is null)
        {
            row = new AppointmentSettingsEntity { Id = IdGenerator.NewId(clock.UtcNow), TenantId = db.CurrentTenantId,
                SettingsJson = JsonSerializer.Serialize(AppointmentSettings.Default, Json) };
            db.Add(row);
        }
        var current = JsonSerializer.Deserialize<AppointmentSettings>(row.SettingsJson, Json)
            ?? throw new InvalidOperationException("Persisted appointment settings are invalid.");
        row.SettingsJson = JsonSerializer.Serialize(mutate(current), Json);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
    // These two members belong to desktop file-backup semantics. Server callers cannot read/write arbitrary host paths.
    public Task<bool> ExportToAsync(string destinationPath, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(false); }
    public Task<bool> ImportFromAsync(string sourcePath, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(false); }
}
