using System.Security.Claims;
using Animora.Domain.Results;
using Animora.Server.Admin;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace Animora.Server.Tests.Admin;
/// <summary>HTTP/SSR harness: production routing, forms, antiforgery, cookie policy; deterministic DTO ports only.</summary>
internal sealed class AdminOperationsHost : IDisposable
{
    public const string IntentId = "01K00000000000000000000001";
    public const string ClientId = "01K00000000000000000000002";
    public const string TenantId = "01K00000000000000000000003";
    public const string FileId = "01K00000000000000000000004";
    public const string Secret = "one-time-test-secret-never-on-get";
    public AdminHost Host { get; }
    public Operations Ports { get; } = new();
    public AdminOperationsHost()
    {
        Host = new AdminHost(configure: services =>
        {
            services.PostConfigure<CookieAuthenticationOptions>(OperatorCookie.Scheme, o => o.Events.OnValidatePrincipal = _ => Task.CompletedTask);
            services.AddSingleton<IAdminProvisioningQueries>(Ports); services.AddSingleton<IAdminProvisioningCommands>(Ports);
            services.AddSingleton<IAdminWebsiteClients>(Ports); services.AddSingleton<IAdminFileQueries>(Ports); services.AddSingleton<IAdminFileCommands>(Ports);
            services.AddSingleton<IAdminAuditQueries>(Ports); services.AddSingleton<IAdminInsightStatus>(Ports); services.AddSingleton<IAdminStatusReader>(Ports);
        });
        var options = Host.Factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(OperatorCookie.Scheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new("sub", "test-operator"), new("role", "operator"), new("ost", "test-stamp"),
            new("op_exp", DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds().ToString())], OperatorCookie.Scheme, ClaimTypes.Name, "role"));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30) }, OperatorCookie.Scheme);
        Host.Client.DefaultRequestHeaders.Add("Cookie", OperatorCookie.Name + "=" + options.TicketDataFormat.Protect(ticket));
    }
    public void Dispose() => Host.Dispose();
    internal sealed class Operations : IAdminProvisioningQueries, IAdminProvisioningCommands, IAdminWebsiteClients,
        IAdminFileQueries, IAdminFileCommands, IAdminAuditQueries, IAdminInsightStatus, IAdminStatusReader
    {
        public int RetryCalls, CreateCalls, RevokeCalls, RequeueCalls, AbandonCalls, TenantAuditCalls, AuditCalls;
        private bool _mismatch = true;
        private bool _revoked;
        private static readonly DateTimeOffset Now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        public Task<Result<AdminPage<ProvisioningSummary>>> ListAsync(string? status, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
            Task.FromResult(Result<AdminPage<ProvisioningSummary>>.Ok(new([new(IntentId, "safe-key", ClientId, "Failed", 2, "TRANSIENT", Now, null)], 1, 50, 1)));
        public Task<Result<MutationReceipt>> RetryAsync(string id, bool confirm, CancellationToken ct = default)
        {
            if (!confirm || id != IntentId) return Task.FromResult(Result<MutationReceipt>.Fail(ErrorCodes.ValidationFailed));
            RetryCalls++; AuditCalls++; return Task.FromResult(Result<MutationReceipt>.Ok(new()));
        }
        public Task<Result<AdminPage<WebsiteClientSummary>>> ListAsync(int page = 1, int pageSize = 50, CancellationToken ct = default) =>
            Task.FromResult(Result<AdminPage<WebsiteClientSummary>>.Ok(new([new(ClientId, "safe-key-id", "Website", _revoked ? "Revoked" : "Active", Now, null)], 1, 50, 1)));
        public Task<Result<WebsiteClientSecret>> CreateAsync(WebsiteClientCreateForm form, CancellationToken ct = default)
        {
            if (!form.Confirmed || string.IsNullOrWhiteSpace(form.Label)) return Task.FromResult(Result<WebsiteClientSecret>.Fail(ErrorCodes.ValidationFailed));
            CreateCalls++; AuditCalls++; return Task.FromResult(Result<WebsiteClientSecret>.Ok(new("new-key-id", Secret)));
        }
        public Task<Result<MutationReceipt>> RevokeAsync(string id, bool confirm, CancellationToken ct = default)
        {
            if (!confirm || id != ClientId) return Task.FromResult(Result<MutationReceipt>.Fail(ErrorCodes.ValidationFailed));
            RevokeCalls++; AuditCalls++; _revoked = true; return Task.FromResult(Result<MutationReceipt>.Ok(new()));
        }
        public Task<Result<AdminPage<FileMismatchSummary>>> ListMismatchesAsync(int page = 1, int pageSize = 50, CancellationToken ct = default) =>
            Task.FromResult(Result<AdminPage<FileMismatchSummary>>.Ok(new(_mismatch ? [new(TenantId, "clinic-one", FileId, 123, "01234567", Now)] : [], 1, 50, _mismatch ? 1 : 0)));
        public Task<Result<MutationReceipt>> RequeueAsync(FileMismatchActionForm form, CancellationToken ct = default) => Change(form, true);
        public Task<Result<MutationReceipt>> AbandonAsync(FileMismatchActionForm form, CancellationToken ct = default) => Change(form, false);
        private Task<Result<MutationReceipt>> Change(FileMismatchActionForm form, bool requeue)
        {
            if (!form.Confirmed || form.TenantId != TenantId || form.FileId != FileId) return Task.FromResult(Result<MutationReceipt>.Fail(ErrorCodes.ValidationFailed));
            if (!_mismatch) return Task.FromResult(Result<MutationReceipt>.Fail(ErrorCodes.FileHashMismatch));
            _mismatch = false; if (requeue) RequeueCalls++; else AbandonCalls++; AuditCalls++;
            return Task.FromResult(Result<MutationReceipt>.Ok(new()));
        }
        public Task<Result<AdminPage<AuditSummary>>> OperatorAsync(AdminAuditQuery query, CancellationToken ct = default) => Audit(query);
        public Task<Result<AdminPage<AuditSummary>>> TenantAsync(AdminAuditQuery query, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query.TenantId) || query.From is not { } from || query.To is not { } to || to <= from || to - from > TimeSpan.FromDays(90))
                return Task.FromResult(Result<AdminPage<AuditSummary>>.Fail(ErrorCodes.ValidationFailed));
            TenantAuditCalls++; return Audit(query);
        }
        private static Task<Result<AdminPage<AuditSummary>>> Audit(AdminAuditQuery query)
        {
            var bounds = AdminPage<AuditSummary>.Bounds(query.Page, query.PageSize);
            return Task.FromResult(Result<AdminPage<AuditSummary>>.Ok(new([new("audit-id", Now, "test-operator", "auth.login", query.TenantId, "success", "safe-correlation", "safe detail")], bounds.Page, bounds.Size, 201)));
        }
        public Task<Result<InsightStatus>> GetAsync(CancellationToken ct = default) => Task.FromResult(Result<InsightStatus>.Ok(new(false, false)));
        public Task<Result<AdminStatus>> GetStatusAsync(CancellationToken ct = default) => Task.FromResult(Result<AdminStatus>.Ok(new([], [], new(0, 0, 0, 0), new(0, 12), "test", 10, 0, 0)));
    }
}
