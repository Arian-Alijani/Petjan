using Animora.Application.Security;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Operator;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Animora.Server.Operator;

internal sealed class OperatorDummyCredential(IPasswordHasher<OperatorAccount> hasher)
{
    public OperatorAccount Account { get; } = new();
    public string Hash { get; } = hasher.HashPassword(new OperatorAccount(), "unused-dummy-verification-credential-A1");
}
internal sealed class OperatorAccountService(AnimoraOperatorDbContext db, IPasswordHasher<OperatorAccount> hasher,
    OperatorDummyCredential dummy, IClock clock)
{
    public async Task<Result<OperatorAccount>> LoginAsync(string userName, string password, string correlationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || userName.Length > 64 || string.IsNullOrEmpty(password) || password.Length > 1024)
        {
            _ = hasher.VerifyHashedPassword(dummy.Account, dummy.Hash, "invalid-input");
            return Result<OperatorAccount>.Fail(ErrorCodes.OperatorCredentialsInvalid);
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var normalized = userName.Trim().ToUpperInvariant();
        var account = await db.Accounts.FromSqlInterpolated($"SELECT * FROM public.\"OperatorAccounts\" WHERE \"NormalizedUserName\"={normalized} FOR UPDATE")
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        var verification = hasher.VerifyHashedPassword(account ?? dummy.Account, account?.PasswordHash ?? dummy.Hash, password);
        var allowed = account is { Status: OperatorAccountStatus.Active } && !account.IsLockedOut(clock.UtcNow) && verification != PasswordVerificationResult.Failed;
        if (!allowed)
        {
            if (account is not null && !account.IsLockedOut(clock.UtcNow)) account.FailLogin(clock.UtcNow);
            db.Audit.Add(OperatorAudit.Create(clock.UtcNow, new ActorRef(ActorKind.Operator, account?.Id), "operator.login", null, correlationId, "denied"));
            await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
            return Result<OperatorAccount>.Fail(ErrorCodes.OperatorCredentialsInvalid);
        }
        // The predicate above establishes a non-null active account.
        var loggedIn = account ?? throw new InvalidOperationException("Authenticated operator missing.");
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            loggedIn.PasswordHash = hasher.HashPassword(loggedIn, password);
        loggedIn.SucceedLogin(clock.UtcNow);
        db.Audit.Add(OperatorAudit.Create(clock.UtcNow, new ActorRef(ActorKind.Operator, loggedIn.Id), "operator.login", null, correlationId));
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
        return Result<OperatorAccount>.Ok(loggedIn);
    }

    public async Task<Result> ChangePasswordAsync(string id, string oldPassword, string newPassword, string stamp,
        string correlationId, CancellationToken ct = default)
    {
        var policy = PasswordPolicy.Validate(newPassword);
        if (policy.IsFailure) return policy;
        if (oldPassword.Length > 1024 || newPassword.Length > 1024) return Result.Fail(ErrorCodes.ValidationFailed);
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var account = await db.Accounts.FromSqlInterpolated($"SELECT * FROM public.\"OperatorAccounts\" WHERE \"Id\"={id} FOR UPDATE")
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (account is null || account.SecurityStamp != stamp) return Result.Fail(ErrorCodes.OperatorCredentialsInvalid);
        if (hasher.VerifyHashedPassword(account, account.PasswordHash, oldPassword) == PasswordVerificationResult.Failed)
            return Result.Fail(ErrorCodes.OperatorCredentialsInvalid);
        account.SetPassword(hasher.HashPassword(account, newPassword), clock.UtcNow);
        db.Audit.Add(OperatorAudit.Create(clock.UtcNow, new ActorRef(ActorKind.Operator, id), "operator.password_changed", null, correlationId));
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
        return Result.Ok();
    }

    public async Task<Result> LogoutAsync(string id, string correlationId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var account = await db.Accounts.FromSqlInterpolated($"SELECT * FROM public.\"OperatorAccounts\" WHERE \"Id\"={id} FOR UPDATE")
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (account is null) return Result.Fail(ErrorCodes.OperatorCredentialsInvalid);
        account.SecurityStamp = Animora.Domain.Primitives.IdGenerator.NewId(clock.UtcNow);
        db.Audit.Add(OperatorAudit.Create(clock.UtcNow, new ActorRef(ActorKind.Operator, id), "operator.logout", null, correlationId));
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
        return Result.Ok();
    }
}
