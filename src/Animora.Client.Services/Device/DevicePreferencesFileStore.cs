using System.Text.Json;
using Animora.Contracts.Device;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Extensions.Options;

namespace Animora.Client.Services.Device;

/// <summary>
/// Local JSON-file implementation of <see cref="IDevicePreferencesService"/>, kept beside the database in
/// <c>&lt;DataDirectory&gt;/device-prefs.json</c>. Readable before any sign-in (the file is device-scoped,
/// not tenant/user-scoped). All reads/writes are serialized by a single gate and I/O runs off the UI thread;
/// a missing or corrupt file degrades gracefully to <see cref="DevicePreferences.Default"/> and is never
/// allowed to throw into the startup path. Registered as a singleton so the in-memory cache is shared.
/// </summary>
public sealed class DevicePreferencesFileStore : IDevicePreferencesService
{
    internal const string FileName = "device-prefs.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DevicePreferences? _cache;

    public DevicePreferencesFileStore(IOptions<LocalStoreOptions> store)
        : this(Path.Combine(store.Value.DataDirectory, FileName)) { }

    internal DevicePreferencesFileStore(string path) => _path = path;

    public async Task<DevicePreferences> GetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await LoadAsync(ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public Task SaveAsync(DevicePreferences preferences, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return MutateAsync(_ => preferences, ct);
    }

    public Task SetRememberMeAsync(bool rememberMe, CancellationToken ct = default) =>
        MutateAsync(current => current.RememberMe == rememberMe ? null : current with { RememberMe = rememberMe }, ct);

    public Task SetStartWithWindowsAsync(bool startWithWindows, CancellationToken ct = default) =>
        MutateAsync(current => current.StartWithWindows == startWithWindows
            ? null
            : current with { StartWithWindows = startWithWindows }, ct);

    /// <summary>Serialized read-modify-write; <paramref name="change"/> returns null for "nothing to persist".</summary>
    private async Task MutateAsync(Func<DevicePreferences, DevicePreferences?> change, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await LoadAsync(ct).ConfigureAwait(false);
            if (change(current) is { } next) await PersistAsync(next, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    // Callers hold _gate. File I/O runs on the thread pool so the startup path never stats/reads on the UI thread.
    private async Task<DevicePreferences> LoadAsync(CancellationToken ct)
    {
        if (_cache is not null) return _cache;
        try
        {
            var text = await Task.Run<string?>(async () =>
                File.Exists(_path) ? await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false) : null, ct)
                .ConfigureAwait(false);
            if (text is null) return _cache = DevicePreferences.Default;
            var loaded = JsonSerializer.Deserialize<DevicePreferences>(text, Json);
            return _cache = loaded ?? DevicePreferences.Default;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A missing/locked/corrupt preferences file must never block the login screen: serve defaults.
            return _cache = DevicePreferences.Default;
        }
    }

    // Callers hold _gate.
    private async Task PersistAsync(DevicePreferences preferences, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(preferences, Json);
        try
        {
            await Task.Run(async () =>
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var temp = _path + ".tmp";
                await File.WriteAllTextAsync(temp, json, ct).ConfigureAwait(false);
                File.Move(temp, _path, overwrite: true);
            }, ct).ConfigureAwait(false);
            _cache = preferences;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: keep the new value in the in-memory cache for this session even if the disk write failed.
            _cache = preferences;
        }
    }
}
