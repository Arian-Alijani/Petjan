using System.Text.Json;
using Animora.Application.Ports;
using Animora.Contracts.Visits;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Extensions.Options;

namespace Animora.Client.Services.Visits;

/// <summary>
/// Local JSON-file implementation of <see cref="ILabTestCatalogStore"/>, kept beside the database in
/// <c>&lt;DataDirectory&gt;/lab-test-types.json</c> (the same approach as <c>AppointmentSettingsFileStore</c>): genuinely
/// asynchronous I/O, every read/write serialized by one gate, atomic temp-file replace, and a corrupt file is
/// quarantined next to the original rather than silently overwritten. Registered as a singleton (the cache relies on
/// a single instance). Authorization lives in the Application use cases that call this port — never here.
/// </summary>
public sealed class LabTestCatalogFileStore : ILabTestCatalogStore
{
    internal const string FileName = "lab-test-types.json";
    private const int MaxNameLength = 60;
    private const int MaxUnitLength = 20;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    // Common veterinary panel shown on a fresh install so the lab form is usable immediately; fully editable.
    internal static readonly IReadOnlyList<LabTestType> Defaults =
    [
        new("lt-wbc", "گلبول سفید (WBC)", "×10³/µL"),
        new("lt-rbc", "گلبول قرمز (RBC)", "×10⁶/µL"),
        new("lt-hgb", "هموگلوبین (HGB)", "g/dL"),
        new("lt-hct", "هماتوکریت (HCT)", "%"),
        new("lt-plt", "پلاکت (PLT)", "×10³/µL"),
        new("lt-glu", "قند خون (GLU)", "mg/dL"),
        new("lt-bun", "اوره (BUN)", "mg/dL"),
        new("lt-cre", "کراتینین (CREA)", "mg/dL"),
        new("lt-alt", "آلانین ترانسفراز (ALT)", "U/L"),
        new("lt-ast", "آسپارتات ترانسفراز (AST)", "U/L"),
    ];

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<LabTestType>? _cache;
    private bool _unreadableOriginal;

    public LabTestCatalogFileStore(IOptions<LocalStoreOptions> store)
        : this(Path.Combine(store.Value.DataDirectory, FileName)) { }

    internal LabTestCatalogFileStore(string path) => _path = path;

    public async Task<IReadOnlyList<LabTestType>> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await LoadAsync(ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task<LabTestType?> AddTypeAsync(string name, string? defaultUnit, CancellationToken ct)
    {
        name = Clean(name, MaxNameLength) ?? throw new ArgumentException("Type name is required.", nameof(name));
        var unit = Clean(defaultUnit, MaxUnitLength);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await LoadAsync(ct).ConfigureAwait(false);
            if (current.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))) return null;
            var created = new LabTestType("lt-" + Guid.NewGuid().ToString("N")[..8], name, unit);
            await PersistAsync([.. current, created], ct).ConfigureAwait(false);
            return created;
        }
        finally { _gate.Release(); }
    }

    public async Task RemoveTypeAsync(string typeId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await LoadAsync(ct).ConfigureAwait(false);
            var kept = current.Where(t => t.Id != typeId).ToArray();
            if (kept.Length != current.Count) await PersistAsync(kept, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task ResetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() => { if (File.Exists(_path)) File.Delete(_path); }, ct).ConfigureAwait(false);
            _unreadableOriginal = false;
            _cache = Defaults;
        }
        finally { _gate.Release(); }
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
                await File.WriteAllBytesAsync(destinationPath, await File.ReadAllBytesAsync(_path, ct).ConfigureAwait(false), ct)
                    .ConfigureAwait(false);
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
            var restored = await Task.Run<IReadOnlyList<LabTestType>?>(async () =>
            {
                if (!File.Exists(sourcePath)) return null;
                try { return Normalize(JsonSerializer.Deserialize<List<LabTestType>>(
                    await File.ReadAllTextAsync(sourcePath, ct).ConfigureAwait(false), Json)); }
                catch (JsonException) { return null; }
            }, ct).ConfigureAwait(false);
            if (restored is null) return false;

            // Keep the catalog being replaced next to the live file; a restore never destroys data.
            await Task.Run(() =>
            {
                if (File.Exists(_path)) File.Move(_path, $"{_path}.replaced-{DateTime.UtcNow:yyyyMMddHHmmssfff}", overwrite: false);
            }, ct).ConfigureAwait(false);
            await PersistAsync(restored, ct).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    private static string? Clean(string? value, int max)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return null;
        return text.Length > max ? text[..max] : text;
    }

    /// <summary>Drops malformed / duplicate rows so a hand-edited or partly written file can never break the lab form.</summary>
    private static IReadOnlyList<LabTestType> Normalize(IEnumerable<LabTestType>? types)
    {
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<LabTestType>();
        foreach (var type in types ?? [])
        {
            if (type is null || string.IsNullOrWhiteSpace(type.Id)) continue;
            var name = Clean(type.Name, MaxNameLength);
            if (name is null || !seenIds.Add(type.Id) || !seenNames.Add(name)) continue;
            result.Add(new LabTestType(type.Id, name, Clean(type.DefaultUnit, MaxUnitLength)));
        }

        return result;
    }

    // Callers hold _gate. File-system work runs on the thread pool so a UI-triggered call never stats/reads on the dispatcher thread.
    private async Task<IReadOnlyList<LabTestType>> LoadAsync(CancellationToken ct)
    {
        if (_cache is not null) return _cache;
        try
        {
            var text = await Task.Run<string?>(async () =>
                File.Exists(_path) ? await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false) : null, ct).ConfigureAwait(false);
            if (text is null) return _cache = Defaults;
            var loaded = JsonSerializer.Deserialize<List<LabTestType>>(text, Json);
            return loaded is null ? _cache = await QuarantineAsync(ct).ConfigureAwait(false) : _cache = Normalize(loaded);
        }
        catch (JsonException)
        {
            return _cache = await QuarantineAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Transient/permission failure: leave the file untouched, serve defaults for this process; the next write moves it aside.
            _unreadableOriginal = true;
            return _cache = Defaults;
        }
    }

    // Moves the unreadable original aside (never overwrites it) and serves the defaults.
    private Task<IReadOnlyList<LabTestType>> QuarantineAsync(CancellationToken ct) => Task.Run(() =>
    {
        try { File.Move(_path, $"{_path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}", overwrite: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _unreadableOriginal = true; }
        return Defaults;
    }, ct);

    // Callers hold _gate.
    private async Task PersistAsync(IReadOnlyList<LabTestType> types, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(types, Json);
        await Task.Run(async () =>
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            if (_unreadableOriginal && File.Exists(_path))
                File.Move(_path, $"{_path}.unreadable-{DateTime.UtcNow:yyyyMMddHHmmssfff}", overwrite: false);
            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, json, ct).ConfigureAwait(false);
            File.Move(temp, _path, overwrite: true);
        }, ct).ConfigureAwait(false);
        _unreadableOriginal = false;
        _cache = types;
    }
}
