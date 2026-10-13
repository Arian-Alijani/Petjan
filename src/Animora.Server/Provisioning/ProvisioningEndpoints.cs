using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Provisioning;
using Animora.Persistence.PostgreSql.Subscriptions;
using Animora.Server.Hosting;
using Animora.Server.Operator;
using Animora.Server.Operator.Models;
using Microsoft.AspNetCore.DataProtection;
namespace Animora.Server.Provisioning;

internal static class ProvisioningEndpoints
{
    internal const int MaxBodyBytes = 8192;
    internal static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength > MaxBodyBytes) return null;
        var buffer = new byte[MaxBodyBytes + 1]; var used = 0;
        while (used < buffer.Length)
        {
            var n = await request.Body.ReadAsync(buffer.AsMemory(used), ct).ConfigureAwait(false);
            if (n == 0) return buffer.AsSpan(0, used).ToArray(); used += n;
        }
        return null;
    }
    public static IEndpointRouteBuilder MapProvisioningEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ServerRoutes.Provisioning).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Operator);
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext; http.Response.Headers.CacheControl = "no-store";
            if (!http.Request.IsHttps && !http.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment())
                return Results.Json(new { code = ErrorCodes.ProvisioningSignatureInvalid.Code }, statusCode: 401);
            return await next(context).ConfigureAwait(false);
        });
        group.MapPost("/intents", async (HttpContext http, RequestSignatureVerifier verifier, IProvisioningIntentStore store,
            IDataProtectionProvider protection, IClock clock, CancellationToken ct) =>
        {
            var body = await ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
            if (body is null) return Results.StatusCode(413);
            var identity = await verifier.AuthenticateAsync(http, body, ct).ConfigureAwait(false);
            if (identity.IsFailure) return Results.Json(new { code = ErrorCodes.ProvisioningSignatureInvalid.Code }, statusCode: 401);
            CreateTenantRequest? r;
            try { r = JsonSerializer.Deserialize<CreateTenantRequest>(body, JsonSerializerOptions.Web); }
            catch (JsonException) { return OperatorEndpoints.Failure(ErrorCodes.ValidationFailed, http); }
            var key = http.Request.Headers["Idempotency-Key"].ToString();
            if (r is null || key.Length is < 1 or > 128 || string.IsNullOrWhiteSpace(r.TenantCode) ||
                string.IsNullOrWhiteSpace(r.DisplayName) || string.IsNullOrWhiteSpace(r.OwnerUserName) || string.IsNullOrWhiteSpace(r.OwnerDisplayName))
                return OperatorEndpoints.Failure(ErrorCodes.ValidationFailed, http);
            var request = new ProvisioningRequest(key, r.TenantCode, r.DisplayName, r.LegalName, r.Timezone,
                r.OwnerUserName, r.OwnerDisplayName, r.OwnerInitialPassword, r.PlanCode, r.SeatLimitOverride, r.StartTrial);
            var now = clock.UtcNow;
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(request, JsonSerializerOptions.Web);
            byte[] protectedPayload;
            try { protectedPayload = protection.CreateProtector("Animora.Provisioning.Intent.v1").Protect(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
            var row = new ProvisioningIntent { Id = IdGenerator.NewId(now), IdempotencyKey = key,
                ClientId = identity.Value ?? string.Empty, RequestFingerprint = ProvisioningEngine.Fingerprint(request),
                PayloadProtected = protectedPayload, NextAttemptUtc = now, ReceivedUtc = now };
            var receipt = await store.InsertAsync(row, ct).ConfigureAwait(false);
            return receipt.IsSuccess ? Results.Json(receipt.Value, statusCode: 202) : OperatorEndpoints.Failure(receipt.Error, http);
        });
        group.MapGet("/intents/{idempotencyKey}", async (string idempotencyKey, HttpContext http, RequestSignatureVerifier verifier,
            IProvisioningIntentStore store, IDataProtectionProvider protection, IClock clock, CancellationToken ct) =>
        {
            var body = await ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
            if (body is null) return Results.StatusCode(413);
            var identity = await verifier.AuthenticateAsync(http, body, ct).ConfigureAwait(false);
            if (identity.IsFailure) return Results.Json(new { code = ErrorCodes.ProvisioningSignatureInvalid.Code }, statusCode: 401);
            var polled = await store.PollAsync(idempotencyKey, identity.Value ?? string.Empty, clock.UtcNow, ct).ConfigureAwait(false);
            if (polled.IsFailure || polled.Value is not { } result) return OperatorEndpoints.Failure(polled.Error, http);
            JsonElement? data; string? password;
            // IMP-08 mirror: a corrupted ResultJson or a TempPassword protected under a rotated/purged key must map to a
            // typed error, not a raw 500 that leaves the intent un-pollable. (POST already guards Protect with try/finally.)
            try
            {
                data = result.ResultJson is null ? null : JsonSerializer.Deserialize<JsonElement>(result.ResultJson);
                password = result.ProtectedTempPassword is null ? null : protection.CreateProtector("Animora.Provisioning.Intent.v1")
                    .Unprotect(Encoding.UTF8.GetString(result.ProtectedTempPassword));
            }
            catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException)
            {
                return OperatorEndpoints.Failure(ErrorCodes.ProvisioningSignatureInvalid, http);
            }
            return Results.Ok(new { status = result.Status.ToString(), tenantId = Property(data, "tenantId"),
                tenantCode = Property(data, "tenantCode"), subscriptionId = Property(data, "subscriptionId"), ownerTemporaryPassword = password, result.ErrorCode });
        });
        return app;
    }
    private static string? Property(JsonElement? data, string key) => data is { ValueKind: JsonValueKind.Object } value &&
        value.TryGetProperty(key, out var property) ? property.GetString() : null;
}
