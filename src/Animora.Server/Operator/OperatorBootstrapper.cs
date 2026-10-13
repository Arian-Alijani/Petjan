using Animora.Application.Security;
using Animora.Domain.Primitives;
using Animora.Domain.Subscriptions;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Operator;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Animora.Server.Operator;

internal sealed class OperatorBootstrapper(IServiceScopeFactory scopes, ILogger<OperatorBootstrapper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        // Deliberately read only environment variables; configuration providers cannot bootstrap credentials.
        var userName = Environment.GetEnvironmentVariable("Operator__BootstrapUserName");
        var password = Environment.GetEnvironmentVariable("Operator__BootstrapPassword");
        // SEC-OPER-01 (defense-in-depth): the bootstrap password stays in the process environment after this runs; a
        // process cannot reliably scrub a parent-provided env var. Operators should unset
        // Operator__BootstrapUserName / Operator__BootstrapPassword after first boot (see the server runbook).
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password)) return;
        if (userName.Trim().Length > 64 || PasswordPolicy.Validate(password).IsFailure)
            throw new InvalidOperationException("Invalid operator bootstrap configuration.");
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraOperatorDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(140014)", ct).ConfigureAwait(false);
        if (await db.Accounts.AnyAsync(ct).ConfigureAwait(false)) return;
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        var account = new OperatorAccount { Id = IdGenerator.NewId(now), UserName = userName.Trim(),
            NormalizedUserName = userName.Trim().ToUpperInvariant(), SecurityStamp = IdGenerator.NewId(now),
            CreatedUtc = now, MustChangePassword = true };
        account.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<OperatorAccount>>().HashPassword(account, password);
        db.Accounts.Add(account);
        db.Audit.Add(OperatorAudit.Create(now, new ActorRef(ActorKind.System), "operator.bootstrap.created", null, account.Id));
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
        logger.LogInformation("operator.bootstrap.created");
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
