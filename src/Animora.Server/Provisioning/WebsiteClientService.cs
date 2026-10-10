using System.Security.Cryptography;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Provisioning;
using Microsoft.AspNetCore.DataProtection;
namespace Animora.Server.Provisioning;

internal sealed record WebsiteClientCreated(string Id, string KeyId, string Secret, string SecretEncoding = "base64");
internal sealed class WebsiteClientService(WebsiteClientStore store, IDataProtectionProvider protection, IClock clock)
{
    public async Task<Result<WebsiteClientCreated>> CreateAsync(string label, ActorRef actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length > 100) return Result<WebsiteClientCreated>.Fail(ErrorCodes.ValidationFailed);
        var now = clock.UtcNow; var secret = RandomNumberGenerator.GetBytes(32);
        try
        {
            var row = new WebsiteClient { Id = IdGenerator.NewId(now), KeyId = IdGenerator.NewId(now), Label = label.Trim(), CreatedUtc = now,
                ProtectedSecret = protection.CreateProtector("Animora.Provisioning.Client.v1").Protect(secret) };
            var result = await store.CreateAsync(row, actor, ct).ConfigureAwait(false);
            return result.IsSuccess ? Result<WebsiteClientCreated>.Ok(new(row.Id, row.KeyId, Convert.ToBase64String(secret)))
                : Result<WebsiteClientCreated>.Fail(result.Error, result.Detail);
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }
    public Task<Result> RevokeAsync(string id, ActorRef actor, CancellationToken ct = default) => store.RevokeAsync(id, actor, clock.UtcNow, ct);
}
