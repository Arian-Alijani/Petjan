using System.Text.Json;
using System.Text.Json.Serialization;
using Animora.Application.Ports;
using Animora.Contracts.Appointments;
using Animora.Domain.Scheduling;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Extensions.Options;

namespace Animora.Client.Services.Appointments;

/// <summary>
/// Local JSON-file implementation of <see cref="IAppointmentSettingsStore"/>, kept beside the database in
/// <c>&lt;DataDirectory&gt;/appointment-settings.json</c>. I/O is genuinely asynchronous and every read/write is
/// serialized by one gate (A23); a corrupt file is quarantined next to the original and reported through
/// <see cref="AppointmentSettings.RecoveryNotice"/> instead of being silently overwritten (A26).
/// Registered as a singleton: the cache relies on a single instance. Authorization lives in the Application use
/// cases that call this port — never here.
/// </summary>
public sealed class AppointmentSettingsFileStore : IAppointmentSettingsStore
{
    internal const string FileName = "appointment-settings.json";
    private const int MaxLabelLength = 40;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppointmentSettings? _cache;
    // True when the file exists but could not be read (IO/permission): the next write moves it aside first.
    private bool _unreadableOriginal;

    public AppointmentSettingsFileStore(IOptions<LocalStoreOptions> store)
        : this(Path.Combine(store.Value.DataDirectory, FileName)) { }

    internal AppointmentSettingsFileStore(string path) => _path = path;

    public async Task<AppointmentSettings> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await LoadAsync(ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public Task SaveAsync(AppointmentSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return MutateAsync(_ => Normalize(settings) with { RecoveryNotice = null }, ct);
    }

    public async Task<CustomAppointmentType> AddTypeAsync(string label, int durationMinutes, bool requiresDoctor,
        CancellationToken ct)
    {
        label = (label ?? string.Empty).Trim();
        if (label.Length == 0) throw new ArgumentException("Type label is required.", nameof(label));
        if (label.Length > MaxLabelLength) label = label[..MaxLabelLength];

        CustomAppointmentType? created = null;
        await MutateAsync(current =>
        {
            // Colors already in use: the catalog's fixed types (by order) + existing custom types.
            var used = Enumerable.Range(0, AppointmentTypeCatalog.All.Count).Select(AppointmentColorPalette.ByIndex)
                .Concat(current.CustomTypes.Select(t => t.ColorHex));
            created = new CustomAppointmentType(
                "c-" + Guid.NewGuid().ToString("N")[..8], label,
                AppointmentColorPalette.PickUnused(used), ClampDuration(durationMinutes), requiresDoctor);
            return current with { CustomTypes = [.. current.CustomTypes, created] };
        }, ct).ConfigureAwait(false);
        return created!;
    }

    public Task RemoveTypeAsync(string typeId, CancellationToken ct) => MutateAsync(current =>
    {
        var kept = current.CustomTypes.Where(t => t.Id != typeId).ToArray();
        return kept.Length == current.CustomTypes.Count ? null : current with { CustomTypes = kept };
    }, ct);

    public Task SetCalendarModeAsync(CalendarMode mode, CancellationToken ct) => MutateAsync(
        current => current.CalendarMode == mode ? null : current with { CalendarMode = mode }, ct);

    public Task SetSmsAsync(AppointmentSmsSettings sms, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sms);
        var next = sms with { ReminderLeadHours = Math.Clamp(sms.ReminderLeadHours, 1, 168) };
        return MutateAsync(current => current.Sms == next ? null : current with { Sms = next }, ct);
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

    /// <summary>Serialized read-modify-write; <paramref name="change"/> returns null for "nothing to persist".</summary>
    private async Task MutateAsync(Func<AppointmentSettings, AppointmentSettings?> change, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await LoadAsync(ct).ConfigureAwait(false);
            if (change(current) is { } next) await PersistAsync(next, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    // Callers hold _gate. All file-system work runs on the thread pool: the first awaited call of a UI-triggered
    // settings operation must never stat/read/move files on the dispatcher thread (A23).
    private async Task<AppointmentSettings> LoadAsync(CancellationToken ct)
    {
        if (_cache is not null) return _cache;
        try
        {
            var text = await Task.Run<string?>(async () =>
                File.Exists(_path) ? await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false) : null, ct)
                .ConfigureAwait(false);
            if (text is null) return _cache = AppointmentSettings.Default;
            var loaded = JsonSerializer.Deserialize<AppointmentSettings>(text, Json);
            if (loaded is not null) return _cache = Normalize(loaded);
            return _cache = await QuarantineAsync(ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return _cache = await QuarantineAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Transient/permission failure: do not touch the file, serve defaults for this process, and tell the user.
            // The original stays untouched; PersistAsync preserves it before the first write replaces it.
            _unreadableOriginal = true;
            return _cache = AppointmentSettings.Default with
            {
                RecoveryNotice = "فایل تنظیمات نوبت خوانده نشد؛ مقادیر پیش‌فرض نمایش داده می‌شود.",
            };
        }
    }

    // Moves the unreadable original aside (never overwrites it) and serves defaults with a visible notice.
    private Task<AppointmentSettings> QuarantineAsync(CancellationToken ct) => Task.Run(() =>
    {
        var notice = "فایل تنظیمات نوبت آسیب‌دیده بود؛ نسخهٔ اصلی کنار فایل نگه داشته شد و مقادیر پیش‌فرض بارگذاری شد.";
        try
        {
            var corrupt = $"{_path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            File.Move(_path, corrupt, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notice = "فایل تنظیمات نوبت آسیب‌دیده است و قابل انتقال نبود؛ مقادیر پیش‌فرض نمایش داده می‌شود.";
        }
        return AppointmentSettings.Default with { RecoveryNotice = notice };
    }, ct);

    // Callers hold _gate.
    private async Task PersistAsync(AppointmentSettings settings, CancellationToken ct)
    {
        var clean = settings with { RecoveryNotice = null };
        var json = JsonSerializer.Serialize(clean, Json);
        await Task.Run(async () =>
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            // A26: a file we could not read is never silently overwritten - keep it beside the new one.
            if (_unreadableOriginal && File.Exists(_path))
                File.Move(_path, $"{_path}.unreadable-{DateTime.UtcNow:yyyyMMddHHmmssfff}", overwrite: false);
            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, json, ct).ConfigureAwait(false);
            File.Move(temp, _path, overwrite: true);
        }, ct).ConfigureAwait(false);
        _unreadableOriginal = false;
        _cache = clean;
    }

    public async Task<bool> ExportToAsync(string destinationPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(async () =>
            {
                if (!File.Exists(_path)) return false;
                var bytes = await File.ReadAllBytesAsync(_path, ct).ConfigureAwait(false);
                await File.WriteAllBytesAsync(destinationPath, bytes, ct).ConfigureAwait(false);
                return true;
            }, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> ImportFromAsync(string sourcePath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var restored = await Task.Run<AppointmentSettings?>(async () =>
            {
                if (!File.Exists(sourcePath)) return null;
                var text = await File.ReadAllTextAsync(sourcePath, ct).ConfigureAwait(false);
                try { return JsonSerializer.Deserialize<AppointmentSettings>(text, Json) is { } parsed ? Normalize(parsed) : null; }
                catch (JsonException) { return null; }
            }, ct).ConfigureAwait(false);
            if (restored is null) return false;

            // Keep the settings being replaced next to the live file; a restore never destroys data.
            await Task.Run(() =>
            {
                if (File.Exists(_path))
                    File.Move(_path, $"{_path}.replaced-{DateTime.UtcNow:yyyyMMddHHmmssfff}", overwrite: false);
            }, ct).ConfigureAwait(false);
            await PersistAsync(restored, ct).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }
}
