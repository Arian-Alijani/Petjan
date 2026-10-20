using System.Text.Json.Serialization;

namespace Animora.LiveTest.Capacity;

/// <summary>One server-side hardware sample taken over SSH (read-only). Percentages are 0–100, memory in MiB.</summary>
public sealed record ServerSample(
    [property: JsonPropertyName("t")] DateTimeOffset At,
    [property: JsonPropertyName("cpuPct")] double CpuPercent,
    [property: JsonPropertyName("load1")] double Load1,
    [property: JsonPropertyName("memTotalMb")] double MemTotalMb,
    [property: JsonPropertyName("memAvailMb")] double MemAvailableMb,
    [property: JsonPropertyName("swapUsedMb")] double SwapUsedMb,
    [property: JsonPropertyName("established")] int Established,
    [property: JsonPropertyName("serviceActive")] bool? ServiceActive,
    [property: JsonPropertyName("serviceRssMb")] double? ServiceRssMb,
    [property: JsonPropertyName("serviceCpuPct")] double? ServiceCpuPercent,
    [property: JsonPropertyName("pgTotal")] int? PgTotal,
    [property: JsonPropertyName("pgActive")] int? PgActive,
    [property: JsonPropertyName("pgIdleInTx")] int? PgIdleInTransaction)
{
    /// <summary>Memory in use (total − available), MiB.</summary>
    [JsonIgnore]
    public double MemUsedMb => Math.Max(0, MemTotalMb - MemAvailableMb);
}

/// <summary>One probe of the anonymous <c>/health/ready</c> endpoint (works with or without SSH).</summary>
public sealed record HealthSample(
    [property: JsonPropertyName("t")] DateTimeOffset At,
    [property: JsonPropertyName("status")] int? StatusCode,
    [property: JsonPropertyName("ms")] double LatencyMs)
{
    [JsonIgnore]
    public bool Ready => StatusCode is >= 200 and < 300;
}

/// <summary>Peaks / minimums over a time window (a whole run or one concurrency level).</summary>
public sealed record MonitorSummary(
    [property: JsonPropertyName("serverMetrics")] bool ServerMetricsAvailable,
    [property: JsonPropertyName("samples")] int Samples,
    [property: JsonPropertyName("cpuPeakPct")] double CpuPeakPercent,
    [property: JsonPropertyName("cpuAvgPct")] double CpuAveragePercent,
    [property: JsonPropertyName("load1Peak")] double Load1Peak,
    [property: JsonPropertyName("memUsedPeakMb")] double MemUsedPeakMb,
    [property: JsonPropertyName("memAvailMinMb")] double MemAvailableMinMb,
    [property: JsonPropertyName("memTotalMb")] double MemTotalMb,
    [property: JsonPropertyName("swapUsedPeakMb")] double SwapUsedPeakMb,
    [property: JsonPropertyName("establishedPeak")] int EstablishedPeak,
    [property: JsonPropertyName("serviceRssPeakMb")] double? ServiceRssPeakMb,
    [property: JsonPropertyName("serviceCpuPeakPct")] double? ServiceCpuPeakPercent,
    [property: JsonPropertyName("serviceCpuAvgPct")] double? ServiceCpuAveragePercent,
    [property: JsonPropertyName("serviceDownSamples")] int ServiceDownSamples,
    [property: JsonPropertyName("pgConnectionsPeak")] int? PgConnectionsPeak,
    [property: JsonPropertyName("pgMaxConnections")] int? PgMaxConnections,
    [property: JsonPropertyName("healthProbes")] int HealthProbes,
    [property: JsonPropertyName("healthNotReady")] int HealthNotReady,
    [property: JsonPropertyName("healthP95Ms")] double HealthP95Ms)
{
    /// <summary>Summary with no server-side data (SSH disabled or window empty).</summary>
    public static MonitorSummary Empty { get; } = new(
        ServerMetricsAvailable: false,
        Samples: 0,
        CpuPeakPercent: 0,
        CpuAveragePercent: 0,
        Load1Peak: 0,
        MemUsedPeakMb: 0,
        MemAvailableMinMb: 0,
        MemTotalMb: 0,
        SwapUsedPeakMb: 0,
        EstablishedPeak: 0,
        ServiceRssPeakMb: null,
        ServiceCpuPeakPercent: null,
        ServiceCpuAveragePercent: null,
        ServiceDownSamples: 0,
        PgConnectionsPeak: null,
        PgMaxConnections: null,
        HealthProbes: 0,
        HealthNotReady: 0,
        HealthP95Ms: 0);
}
