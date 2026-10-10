using System.Net;
using System.Text;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Audit;
using Animora.Server.Admin;
using Animora.Web.Admin.Models;
using Microsoft.AspNetCore.Http;
using Xunit;
namespace Animora.Server.Tests.Admin;
public sealed class AdminProvisioningPagesTests
{
    [Fact] public async Task Retry_PostOnly_Confirm_NoPayloadOrPasswordShown()
    {
        using var ops = new AdminOperationsHost(); const string route = "/admin/provisioning";
        string html = await ops.Host.Client.GetStringAsync(route + "?status=Failed");
        Assert.Contains("Failed", html); Assert.DoesNotContain("PayloadProtected", html); Assert.DoesNotContain("ProtectedTempPassword", html);
        Assert.Equal(0, ops.Ports.RetryCalls);
        var fields = new Dictionary<string, string> { ["Action.Id"] = AdminOperationsHost.IntentId };
        using var denied = await ops.Host.SubmitAsync(route, "retry-" + AdminOperationsHost.IntentId, fields);
        Assert.Contains("Check the form fields", await denied.Content.ReadAsStringAsync()); Assert.Equal(0, ops.Ports.RetryCalls);
        fields["Action.Confirmed"] = "true";
        using var accepted = await ops.Host.SubmitAsync(route, "retry-" + AdminOperationsHost.IntentId, fields);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode); Assert.Equal(1, ops.Ports.RetryCalls); Assert.Equal(1, ops.Ports.AuditCalls);
    }
    [Fact] public async Task Adapter_RejectsUnconfirmedRetryBeforeCallingStore()
    {
        var session = new AdminSession(new HttpContextAccessor { HttpContext = AdminAdaptersTests.CookieContext() });
        var adapter = new AdminProvisioningAdapter(null!, null!, session, TimeProvider.System); // Invalid input must never reach collaborators.
        Assert.Equal(ErrorCodes.ValidationFailed, (await adapter.RetryAsync(AdminOperationsHost.IntentId, false)).Error);
    }
}
public sealed class AdminWebsiteClientPagesTests
{
    [Fact] public async Task SecretShownOnce_NeverOnGet()
    {
        using var ops = new AdminOperationsHost(); const string route = "/admin/website-clients";
        Assert.DoesNotContain(AdminOperationsHost.Secret, await ops.Host.Client.GetStringAsync(route));
        using var created = await ops.Host.SubmitAsync(route, "create-website-client", new Dictionary<string, string> { ["CreateForm.Label"] = "Website", ["CreateForm.Confirmed"] = "true" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode); Assert.Contains(AdminOperationsHost.Secret, await created.Content.ReadAsStringAsync());
        Assert.Equal("no-store", created.Headers.CacheControl!.ToString());
        Assert.DoesNotContain(AdminOperationsHost.Secret, await ops.Host.Client.GetStringAsync(route));
        Assert.DoesNotContain(AdminOperationsHost.Secret, created.Headers.Location?.OriginalString ?? ""); Assert.Equal(1, ops.Ports.CreateCalls);
        Assert.DoesNotContain(AdminOperationsHost.Secret, new WebsiteClientSecret("key", AdminOperationsHost.Secret).ToString());
    }
    [Fact] public async Task CreateAndRevoke_RequireConfirmation_AndAntiforgery()
    {
        using var ops = new AdminOperationsHost(); const string route = "/admin/website-clients";
        using var missingToken = await ops.Host.Client.PostAsync(route, new FormUrlEncodedContent(new Dictionary<string, string> { ["CreateForm.Label"] = "Website", ["CreateForm.Confirmed"] = "true" }));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        using var unconfirmed = await ops.Host.SubmitAsync(route, "create-website-client", new Dictionary<string, string> { ["CreateForm.Label"] = "Website" });
        Assert.Contains("Check the form fields", await unconfirmed.Content.ReadAsStringAsync()); Assert.Equal(0, ops.Ports.CreateCalls);
        using var revoked = await ops.Host.SubmitAsync(route, "revoke-client-" + AdminOperationsHost.ClientId, new Dictionary<string, string> { ["RevokeForm.Id"] = AdminOperationsHost.ClientId, ["RevokeForm.Confirmed"] = "true" });
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode); Assert.Equal(1, ops.Ports.RevokeCalls);
    }
}
public sealed class AdminFilesPagesTests
{
    [Theory] [InlineData("requeue")] [InlineData("abandon")]
    public async Task Requeue_And_Abandon_OnlyFromMismatch_Audited_NoPathOrContent(string operation)
    {
        using var ops = new AdminOperationsHost(); const string route = "/admin/files/mismatches";
        string html = await ops.Host.Client.GetStringAsync(route);
        Assert.Contains("01234567", html); Assert.DoesNotContain("OriginalName", html); Assert.DoesNotContain("/secret/storage", html);
        var form = new Dictionary<string, string> { ["Action.TenantId"] = AdminOperationsHost.TenantId, ["Action.FileId"] = AdminOperationsHost.FileId, ["Action.Operation"] = operation, ["Action.Confirmed"] = "true" };
        string handler = operation + "-" + AdminOperationsHost.TenantId + "-" + AdminOperationsHost.FileId;
        string token = await ops.Host.TokenAsync(route);
        using var accepted = await ops.Host.SubmitAsync(route, handler, form);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode); Assert.Equal(1, ops.Ports.AuditCalls);
        // Replay a previously valid form after state changed: the command layer rejects stale state.
        form["_handler"] = handler; form["__RequestVerificationToken"] = token;
        using var stale = await ops.Host.Client.PostAsync(route, new FormUrlEncodedContent(form));
        Assert.True(stale.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest); Assert.Equal(1, ops.Ports.AuditCalls);
        Assert.Equal(operation == "requeue" ? 1 : 0, ops.Ports.RequeueCalls); Assert.Equal(operation == "abandon" ? 1 : 0, ops.Ports.AbandonCalls);
    }
    [Fact] public async Task Adapter_RejectsUnconfirmedFileActionBeforePersistence()
    {
        var session = new AdminSession(new HttpContextAccessor { HttpContext = AdminAdaptersTests.CookieContext() });
        var adapter = new AdminFilesAdapter(null!, session);
        Assert.Equal(ErrorCodes.ValidationFailed, (await adapter.RequeueAsync(new())).Error);
        Assert.Equal(ErrorCodes.ValidationFailed, (await adapter.AbandonAsync(new())).Error);
    }
}
public sealed class AdminAuditPagesTests
{
    [Fact] public async Task TenantAudit_RequiresTenant_RangeCap90d_Paged()
    {
        using var ops = new AdminOperationsHost();
        string empty = await ops.Host.Client.GetStringAsync("/admin/audit?tab=tenant");
        Assert.Contains("Select a tenant", empty); Assert.Equal(0, ops.Ports.TenantAuditCalls);
        string invalid = await ops.Host.Client.GetStringAsync("/admin/audit?tab=tenant&tenantId=" + AdminOperationsHost.TenantId + "&from=2026-01-01&to=2026-05-01");
        Assert.Contains("Check the form fields", invalid); Assert.Equal(0, ops.Ports.TenantAuditCalls);
        string page = await ops.Host.Client.GetStringAsync("/admin/audit?tab=tenant&tenantId=" + AdminOperationsHost.TenantId + "&from=2026-10-01&to=2026-10-11&page=2&pageSize=50");
        Assert.Contains("Page 2", page); Assert.Contains("safe detail", page); Assert.Contains("tenantId=" + AdminOperationsHost.TenantId, page); Assert.Equal(1, ops.Ports.TenantAuditCalls);
    }
    [Fact] public void DetailJson_IsCappedByUtf8Bytes_WithoutSplittingRunes()
    {
        string detail = string.Concat(Enumerable.Repeat("گربه\U0001F408", 1000));
        string truncated = AuditQueries.TruncateDetail(detail)!;
        Assert.True(Encoding.UTF8.GetByteCount(truncated) <= 2048); Assert.EndsWith("…", truncated); Assert.DoesNotContain("�", truncated);
    }
}
