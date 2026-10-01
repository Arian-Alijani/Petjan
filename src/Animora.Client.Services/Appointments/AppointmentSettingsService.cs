using System.Text.Json;
using System.Text.Json.Serialization;
using Animora.Contracts.Appointments;
using Animora.Domain.Scheduling;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Extensions.Options;

namespace Animora.Client.Services.Appointments;

/// <summary>
/// Local JSON-file store for the clinic-editable appointment preferences. These are UI/tenant
/// preferences rather than transactional data, so they live beside the database in
/// <c>&lt;DataDirectory&gt;/appointment-settings.json</c> (atomic write, in-memory cache).
/// Registered as a singleton — the cache relies on a single instance.
/// </summary>
public sealed class AppointmentSettingsService : IAppointmentSettingsService
{
    internal const string FileName = "appointment-settings.json";
    private const int MaxLabelLength = 40;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _gate = new();
    private AppointmentSettings? _cache;

    public AppointmentSettingsService(IOptions<LocalStoreOptions> store)
        : this(Path.Combine(store.Value.DataDirectory, FileName)) { }

    internal AppointmentSettingsService(string path) => _path = path;

    public Task<AppointmentSettings> GetAsync()
    {
        lock (_gate) return Task.FromResult(Load());
    }

    public Task SaveAsync(AppointmentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate) Persist(Normalize(settings));
        return Task.CompletedTask;
    }

    public Task<CustomAppointmentType> AddTypeAsync(string label, int durationMinutes, bool requiresDoctor)
    {
        label = (label ?? string.Empty).Trim();
        if (label.Length == 0) throw new ArgumentException("Type label is required.", nameof(label));
        if (label.Length > MaxLabelLength) label = label[..MaxLabelLength];

        lock (_gate)
        {
            var current = Load();
            // Colors already in use: the catalog's fixed types (by order) + existing custom types.
            var used = Enumerable.Range(0, AppointmentTypeCatalog.All.Count).Select(AppointmentColorPalette.ByIndex)
                .Concat(current.CustomTypes.Select(t => t.ColorHex));
            var type = new CustomAppointmentType(
                "c-" + Guid.NewGuid().ToString("N")[..8], label,
                AppointmentColorPalette.PickUnused(used), ClampDuration(durationMinutes), requiresDoctor);
            Persist(current with { CustomTypes = [.. current.CustomTypes, type] });
            return Task.FromResult(type);
        }
    }

    public Task RemoveTypeAsync(string typeId)
    {
        lock (_gate)
        {
            var current = Load();
            var kept = current.CustomTypes.Where(t => t.Id != typeId).ToArray();
            if (kept.Length != current.CustomTypes.Count) Persist(current with { CustomTypes = kept });
        }
        return Task.CompletedTask;
    }

    public Task SetCalendarModeAsync(CalendarMode mode)
    {
        lock (_gate)
        {
            var current = Load();
            if (current.CalendarMode != mode) Persist(current with { CalendarMode = mode });
        }
        return Task.CompletedTask;
    }

    public Task SetSmsAsync(AppointmentSmsSettings sms)
    {
        ArgumentNullException.ThrowIfNull(sms);
        lock (_gate)
        {
            var next = sms with { ReminderLeadHours = Math.Clamp(sms.ReminderLeadHours, 1, 168) };
            var current = Load();
            if (current.Sms != next) Persist(current with { Sms = next });
        }
        return Task.CompletedTask;
    }

    private static int ClampDuration(int minutes) => Math.Clamp(minutes <= 0 ? 30 : minutes, 5, 480);

    private static AppointmentSettings Normalize(AppointmentSettings s) => s with
    {
        Sms = (s.Sms ?? AppointmentSmsSettings.Default) with
        {
            ReminderLeadHours = Math.Clamp((s.Sms ?? AppointmentSmsSettings.Default).ReminderLeadHours, 1, 168),
        },
        CustomTypes = s.CustomTypes ?? [],
    };

    // Callers hold _gate.
    private AppointmentSettings Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            if (File.Exists(_path))
            {
                var loaded = JsonSerializer.Deserialize<AppointmentSettings>(File.ReadAllText(_path), Json);
                if (loaded is not null) return _cache = Normalize(loaded);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable/corrupt preferences must never block the screen — fall back to defaults.
        }
        return _cache = AppointmentSettings.Default;
    }

    // Callers hold _gate.
    private void Persist(AppointmentSettings settings)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, _path, overwrite: true);
        _cache = settings;
    }
}
