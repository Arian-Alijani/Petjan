namespace Animora.Persistence.PostgreSql.Subscriptions;

/// <summary>Schedules on the caller's existing transaction; must never independently save or commit.</summary>
public interface ILicenseIssuanceScheduler
{
    Task ScheduleAsync(string tenantId, string subscriptionId, CancellationToken ct = default);
}
public sealed class NoopLicenseIssuanceScheduler : ILicenseIssuanceScheduler
{
    public Task ScheduleAsync(string tenantId, string subscriptionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
