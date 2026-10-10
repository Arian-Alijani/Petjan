using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Persistence.PostgreSql.Operator;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Provisioning;

public sealed record WebsiteClientListItem(string Id, string KeyId, string Label, WebsiteClientStatus Status, DateTimeOffset CreatedUtc, DateTimeOffset? LastUsedUtc);
public sealed class WebsiteClientStore(ProvisioningDbContext db)
{
    public async Task<OperatorPage<WebsiteClientListItem>> ListAsync(int page = 1, int size = 50, CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, int.MaxValue / 200); size = Math.Clamp(size, 1, 200);
        var query = db.Clients.AsNoTracking();
        long total = await query.LongCountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size)
            .Select(x => new WebsiteClientListItem(x.Id, x.KeyId, x.Label, x.Status, x.CreatedUtc, x.LastUsedUtc)).ToListAsync(ct).ConfigureAwait(false);
        return new(rows, page, size, total);
    }
    public Task<WebsiteClient?> FindAsync(string keyId, CancellationToken ct = default) =>
        db.Clients.AsNoTracking().SingleOrDefaultAsync(x => x.KeyId == keyId && x.Status == WebsiteClientStatus.Active, ct);
    public async Task<Result> CreateAsync(WebsiteClient row, ActorRef actor, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(150015)", ct).ConfigureAwait(false);
        if (await db.Clients.CountAsync(x => x.Status == WebsiteClientStatus.Active, ct).ConfigureAwait(false) >= 3)
            return Result.Fail(ErrorCodes.ValidationFailed, "website_client_limit");
        db.Clients.Add(row); db.Audit.Add(OperatorAudit.Create(row.CreatedUtc, actor, "website.client.created", null, row.Id));
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return Result.Ok();
    }
    public async Task<Result> RevokeAsync(string id, ActorRef actor, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var row = await db.Clients.SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (row is null) return Result.Fail(ErrorCodes.NotFound);
        row.Status = WebsiteClientStatus.Revoked; db.Audit.Add(OperatorAudit.Create(now, actor, "website.client.revoked", null, row.Id));
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return Result.Ok();
    }
    public Task TouchAsync(string id, DateTimeOffset now, CancellationToken ct = default) =>
        db.Clients.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastUsedUtc, now), ct);
    public async Task DeniedAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        db.Audit.Add(OperatorAudit.Create(now, new ActorRef(ActorKind.WebsiteIntent), "provisioning.signature.denied", null,
            Animora.Domain.Primitives.IdGenerator.NewId(now), "denied")); await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
