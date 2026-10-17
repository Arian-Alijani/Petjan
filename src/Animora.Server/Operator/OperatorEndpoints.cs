using System.Globalization;
using Animora.Contracts.Wire.Errors;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Subscriptions;
using Animora.Server.Authorization;
using Animora.Server.Hosting;
using Animora.Server.Operator.Models;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Animora.Server.Operator;

internal static class OperatorEndpoints
{
    public static IEndpointRouteBuilder MapOperatorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ServerRoutes.Operator).RequireRateLimiting(RateLimitPolicies.Operator);
        group.MapPost("/auth/login", async (OperatorLoginRequest r, OperatorAccountService service, IClock clock,
            IOptionsMonitor<BearerTokenOptions> options, HttpContext http, CancellationToken ct) =>
        {
            var result = await service.LoginAsync(r.UserName, r.Password, CorrelationHeader.Resolve(http), ct).ConfigureAwait(false);
            return result.Value is { } account && result.IsSuccess
                ? Results.Ok(OperatorAuth.Issue(account, clock.UtcNow, options)) : Failure(result.Error, http);
        }).AllowAnonymous();
        var secured = group.MapGroup(string.Empty).RequireAuthorization(CoarsePolicies.OperatorOnly);
        secured.AddEndpointFilter(async (invocation, next) =>
        {
            // Every read is audited in the same transaction as its read snapshot; mutation services audit their own effect.
            var http = invocation.HttpContext;
            if (!HttpMethods.IsGet(http.Request.Method)) return await next(invocation).ConfigureAwait(false);
            var db = http.RequestServices.GetRequiredService<AnimoraOperatorDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(http.RequestAborted).ConfigureAwait(false);
            var response = await next(invocation).ConfigureAwait(false);
            var clock = http.RequestServices.GetRequiredService<IClock>();
            db.Audit.Add(OperatorAudit.Create(clock.UtcNow, Actor(http), "operator.read", null, CorrelationHeader.Resolve(http),
                detail: new { path = http.Request.Path.Value }));
            await db.SaveChangesAsync(http.RequestAborted).ConfigureAwait(false); await tx.CommitAsync(http.RequestAborted).ConfigureAwait(false);
            return response;
        });
        secured.MapPost("/auth/logout", async (OperatorAccountService service, HttpContext http, CancellationToken ct) =>
            ToHttp(await service.LogoutAsync(Actor(http).ActorId ?? string.Empty, CorrelationHeader.Resolve(http), ct).ConfigureAwait(false), http));
        secured.MapPost("/auth/change-password", async (OperatorPasswordRequest r, OperatorAccountService service, HttpContext http, CancellationToken ct) =>
            ToHttp(await service.ChangePasswordAsync(Actor(http).ActorId ?? string.Empty, r.OldPassword, r.NewPassword,
                http.User.FindFirst("ost")?.Value ?? string.Empty, CorrelationHeader.Resolve(http), ct).ConfigureAwait(false), http));
        secured.MapGet("/plans", async (AnimoraOperatorDbContext db, CancellationToken ct) => Results.Ok(await db.Plans.AsNoTracking()
            .OrderBy(x => x.Code).Take(200).ToListAsync(ct).ConfigureAwait(false)));
        secured.MapPost("/plans", async (PlanRequest r, AnimoraOperatorDbContext db, IClock clock, HttpContext http, CancellationToken ct) =>
        {
            var created = Plan.Create(r.Code, r.Name, r.DefaultSeatLimit, r.MonthlyAmountMinor, r.TrialDays, clock.UtcNow);
            if (created.IsFailure || created.Value is not { } plan) return Failure(created.Error, http);
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            if (await db.Plans.AnyAsync(x => x.Code == plan.Code, ct).ConfigureAwait(false)) return Failure(ErrorCodes.ConcurrencyConflict, http);
            db.Plans.Add(plan); db.Audit.Add(OperatorAudit.Create(clock.UtcNow, Actor(http), "plan.created", null, plan.Id));
            await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
            http.Response.Headers.ETag = '"' + plan.CreatedUtc.ToString("O", CultureInfo.InvariantCulture) + '"';
            return Results.Created(ServerRoutes.Operator + "/plans/" + plan.Id, plan);
        });
        secured.MapPost("/plans/{id}/deactivate", async (string id, AnimoraOperatorDbContext db, IClock clock, HttpContext http, CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var plan = await db.Plans.FromSqlInterpolated($"SELECT * FROM public.\"Plans\" WHERE \"Id\"={id} FOR UPDATE")
                .SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (plan is null) return Failure(ErrorCodes.PlanNotFound, http);
            if (!IfMatch(http, plan.CreatedUtc.ToString("O", CultureInfo.InvariantCulture))) return Failure(ErrorCodes.ConcurrencyConflict, http);
            plan.Deactivate(); db.Audit.Add(OperatorAudit.Create(clock.UtcNow, Actor(http), "plan.deactivated", null, plan.Id));
            await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return Results.NoContent();
        });
        secured.MapGet("/tenants", async (AnimoraOperatorDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var page = Number(http, "page", 1, int.MaxValue / 200); var size = Number(http, "pageSize", 50, 200);
            var query = db.Tenants.AsNoTracking().Where(x => !x.Deleted);
            if (Enum.TryParse<TenantStatus>(http.Request.Query["status"], true, out var status)) query = query.Where(x => x.Status == status);
            var q = http.Request.Query["q"].ToString(); if (q.Length > 100) return Failure(ErrorCodes.ValidationFailed, http);
            if (q.Length > 0) query = query.Where(x => x.DisplayName.Contains(q) || x.Code != null && x.Code.Contains(q));
            var rows = await (from tenant in query
                join subscription in db.Subscriptions.AsNoTracking() on tenant.Id equals subscription.TenantId into subscriptions
                from s in subscriptions.DefaultIfEmpty()
                orderby tenant.Id
                select new TenantListItem(tenant.Id, tenant.Code, tenant.DisplayName, tenant.Status,
                    s == null ? null : s.Status, s == null ? null : s.CurrentPeriodEndUtc, s == null ? null : s.SeatLimit,
                    db.StaffMemberships.Count(m => m.TenantId == tenant.Id && !m.Deleted && m.Status == MembershipStatus.Active && m.EndedUtc == null)))
                .Skip((page - 1) * size).Take(size).ToListAsync(ct).ConfigureAwait(false);
            return Results.Ok(new { page, pageSize = size, items = rows });
        });
        secured.MapGet("/tenants/{id}", async (string id, AnimoraOperatorDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var tenant = await db.Tenants.AsNoTracking().Where(x => x.Id == id && !x.Deleted)
                .Select(x => new { x.Id, x.Code, x.DisplayName, x.LegalName, x.Timezone, x.Status }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (tenant is null) return Failure(ErrorCodes.NotFound, http);
            var subscription = await db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == id, ct).ConfigureAwait(false);
            var events = subscription is null ? [] : await db.SubscriptionEvents.AsNoTracking()
                .Where(x => x.SubscriptionId == subscription.Id).OrderByDescending(x => x.Sequence).Take(20).ToListAsync(ct).ConfigureAwait(false);
            if (subscription != null) http.Response.Headers.ETag = '"' + subscription.ConcurrencyStamp + '"';
            return Results.Ok(new { tenant, subscription, events });
        });
        secured.MapPost("/tenants", async (CreateTenantRequest r, ProvisioningEngine engine, HttpContext http, CancellationToken ct) =>
        {
            var key = http.Request.Headers["Idempotency-Key"].ToString();
            var result = await engine.ProvisionAsync(new ProvisioningRequest(key, r.TenantCode, r.DisplayName, r.LegalName,
                r.Timezone, r.OwnerUserName, r.OwnerDisplayName, r.OwnerInitialPassword, r.PlanCode, r.SeatLimitOverride, r.StartTrial), Actor(http), ct).ConfigureAwait(false);
            return result.IsSuccess ? Results.Ok(result.Value) : Failure(result.Error, http);
        });
        foreach (var operation in new[] { "extend", "reduce", "suspend", "reinstate", "cancel", "renew", "seats", "plan" })
        {
            var action = operation;
            secured.MapPost("/tenants/{id}/subscription/" + action, async (string id, SubscriptionMutation r,
                ISubscriptionService service, Animora.Server.Authorization.AuthzDecisionCache decisions, HttpContext http, CancellationToken ct) =>
            {
                var stamp = http.Request.Headers.IfMatch.ToString();
                if (string.IsNullOrWhiteSpace(stamp)) return Failure(ErrorCodes.ConcurrencyConflict, http);
                var result = await service.MutateAsync(id, stamp, r with { Operation = action }, Actor(http), ct).ConfigureAwait(false);
                if (result.IsFailure) return Failure(result.Error, http);
                decisions.InvalidateTenant(id); // PERF-AUTHZ-01: suspension/cancel is effective immediately on this instance.
                if (result.Value is { } s) http.Response.Headers.ETag = '"' + s.ConcurrencyStamp + '"';
                return Results.Ok(result.Value);
            });
        }
        secured.MapGet("/tenants/{id}/subscription/events", async (string id, AnimoraOperatorDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var subId = await db.Subscriptions.AsNoTracking().Where(x => x.TenantId == id).Select(x => x.Id).SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (subId is null) return Failure(ErrorCodes.SubscriptionNotFound, http);
            var after = Number(http, "afterSequence", 0, int.MaxValue, 0); var limit = Number(http, "limit", 50, 200);
            return Results.Ok(await db.SubscriptionEvents.AsNoTracking().Where(x => x.SubscriptionId == subId && x.Sequence > after)
                .OrderBy(x => x.Sequence).Take(limit).ToListAsync(ct).ConfigureAwait(false));
        });
        secured.MapPost("/tenants/{id}/owner/reset-password", async (string id, OwnerCredentialAdministration service, HttpContext http, CancellationToken ct) =>
        {
            var result = await service.ResetAsync(id, http.Request.Headers.IfMatch.ToString(), Actor(http), ct).ConfigureAwait(false);
            return result.IsSuccess ? Results.Ok(new { ownerTemporaryPassword = result.Value }) : Failure(result.Error, http);
        });
        secured.MapPost("/tenants/{id}/staff", async (string id, CreateClinicStaffRequest r, ClinicStaffAdministration service, HttpContext http, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(id, r.Name, r.UserName, r.Role, Actor(http), CorrelationHeader.Resolve(http), ct).ConfigureAwait(false);
            http.Response.Headers.CacheControl = "no-store";
            return result.IsSuccess ? Results.Ok(new { temporaryPassword = result.Value }) : Failure(result.Error, http);
        });
        secured.MapPost("/tenants/{id}/staff/{userId}/reset-password", async (string id, string userId, ClinicStaffAdministration service, HttpContext http, CancellationToken ct) =>
        {
            var result = await service.ResetPasswordAsync(id, userId, Actor(http), CorrelationHeader.Resolve(http), ct).ConfigureAwait(false);
            http.Response.Headers.CacheControl = "no-store";
            return result.IsSuccess ? Results.Ok(new { temporaryPassword = result.Value }) : Failure(result.Error, http);
        });
        return app;
    }

    internal static ActorRef Actor(HttpContext http) => new(ActorKind.Operator, http.User.FindFirst("sub")?.Value);
    internal static bool IfMatch(HttpContext http, string expected) => http.Request.Headers.IfMatch.ToString().Trim('"') == expected;
    internal static int Number(HttpContext http, string key, int fallback, int max, int min = 1) =>
        int.TryParse(http.Request.Query[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? Math.Clamp(value, min, max) : fallback;
    internal static IResult ToHttp(Result result, HttpContext http) => result.IsSuccess ? Results.NoContent() : Failure(result.Error, http);
    internal static IResult Failure(ErrorCode error, HttpContext http) => Results.Json(
        new WireApiError(error.Code, null, [], CorrelationHeader.Resolve(http)), statusCode: error == ErrorCodes.ConcurrencyConflict ? 412 :
            error == ErrorCodes.OperatorCredentialsInvalid ? 401 : error.Category switch
            { ErrorCode.NotFound => 404, ErrorCode.Validation => 400, ErrorCode.Forbidden => 403, _ => 409 });
}
