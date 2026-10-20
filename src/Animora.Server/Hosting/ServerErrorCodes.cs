namespace Animora.Server.Hosting;

/// <summary>
/// Server-transport error codes carried on the wire by <c>WireApiError</c>.
/// These are NOT domain <c>ErrorCodes</c> (no entry is added to
/// <c>Animora.Domain</c>): they describe a transport outcome, not a business
/// rule. <see cref="Unexpected"/> mirrors the string value of
/// <c>ErrorCodes.Unexpected</c> ("UNEXPECTED") by contract.
/// </summary>
internal static class ServerErrorCodes
{
    /// <summary>Unclassified server-side failure (HTTP 500).</summary>
    public const string FileOffsetMismatch = "FILE_OFFSET_MISMATCH";
    public const string FileNotAvailable = "FILE_NOT_AVAILABLE";
    public const string Unexpected = "UNEXPECTED";

    /// <summary>Capacity shed (pool exhausted / gate full): HTTP 503 + Retry-After. Mirrors <c>ErrorCodes.ServerBusy</c>.</summary>
    public const string ServerBusy = "SERVER_BUSY";

    /// <summary>
    /// The backend data store is unreachable / shutting down, or its schema is behind this build (an unapplied
    /// migration): HTTP 503 + Retry-After. Distinct from <see cref="ServerBusy"/> (transient capacity) and from
    /// <see cref="Unexpected"/> (a defect) so this operational, retryable condition is diagnosable in the trace.
    /// </summary>
    public const string ServerUnavailable = "SERVER_UNAVAILABLE";

    /// <summary>A named rate-limit policy rejected the request (HTTP 429).</summary>
    public const string RateLimited = "RATE_LIMITED";
}
