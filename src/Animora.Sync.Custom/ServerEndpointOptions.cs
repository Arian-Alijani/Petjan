namespace Animora.Sync.Custom;

/// <summary>Where and how the desktop reaches the central sync server; bound from configuration section <see cref="SectionName"/>.</summary>
public sealed record ServerEndpointOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Animora:Server";

    /// <summary>Absolute http(s) base address of the sync server; null/blank means offline-only (no engine is wired).</summary>
    public string? BaseAddress { get; set; }

    /// <summary>Per-request timeout for every sync call.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Upper bound on pages in one batch; a larger advertised batch is a protocol violation.</summary>
    public int MaxBatchPages { get; set; } = 4096;

    /// <summary>Upper bound on the compressed bytes of one page; a larger body is a protocol violation.</summary>
    public int MaxPageBytes { get; set; } = 262144;

    /// <summary>How many times one page download is attempted on a transient fault (network/busy) before the pull fails.</summary>
    public int MaxPageAttempts { get; set; } = 3;

    /// <summary>Base of the jittered exponential backoff between page attempts (zero disables the wait).</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>True only when <see cref="BaseAddress"/> is a valid absolute http(s) URL.</summary>
    public bool IsConfigured => TryGetBaseUri(out _);

    /// <summary>Parses <see cref="BaseAddress"/> into an absolute http(s) URI that always ends with '/' (so relative paths append).</summary>
    public bool TryGetBaseUri(out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(BaseAddress)) return false;
        var text = BaseAddress.Trim();
        if (!text.EndsWith('/')) text += "/";
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
        uri = parsed;
        return true;
    }
}
