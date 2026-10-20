namespace Animora.Server.Hosting;

/// <summary>
/// PERF-MC-01: the single source of truth for "scale with the hardware you have".
/// <para>
/// <see cref="LogicalCores"/> is <see cref="Environment.ProcessorCount"/> (floored at 1). On .NET this
/// value already honors a container/cgroup CPU quota (e.g. <c>--cpus=2</c> or a Kubernetes CPU limit),
/// so scaling off it respects the real CPU budget rather than the host's physical core count. When the
/// box is upgraded (more cores / a higher CPU limit) every CPU-bound fan-out that derives its width from
/// this helper widens automatically, with no configuration change.
/// </para>
/// <para>
/// <b>Scope rule:</b> this helper sizes <b>CPU-bound</b> parallelism only (worker fan-out, outbox
/// consumer count, recovery parallelism). It is intentionally <b>not</b> used to size database
/// connection pools or the request-path sync admission gate — those are bounded by the PostgreSQL
/// connection budget (<c>Database:ProfileGiB</c>), and widening them by core count would over-subscribe
/// PostgreSQL. CPU-bound callers that still touch the database must additionally clamp their width to the
/// relevant DB budget (see <see cref="ScaleByCores"/> callers).
/// </para>
/// </summary>
internal static class CpuTopology
{
    /// <summary>Logical CPU count available to this process (cgroup/container-quota aware), floored at 1.</summary>
    public static int LogicalCores { get; } = Math.Max(1, Environment.ProcessorCount);

    /// <summary>
    /// Width = <paramref name="perCore"/> × <see cref="LogicalCores"/>, clamped to [<paramref name="min"/>,
    /// <paramref name="max"/>]. Callers that open database work MUST pass a <paramref name="max"/> no larger
    /// than the DB connection budget for that path so CPU scaling never exhausts the PostgreSQL pool.
    /// </summary>
    public static int ScaleByCores(int perCore, int min, int max) =>
        Math.Clamp(perCore * LogicalCores, min, max);
}
