using System.Net;
using System.Text;
using System.Text.Json;
using Animora.Contracts.Wire;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;
using Animora.Server.Commands;
using Animora.Server.Sync;
using Animora.Server.Tests.Commands;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Animora.Server.Tests;

/// <summary>
/// ARCH §19 gate M (G08): mixed-fleet behaviour across wire versions N / N-1 and rule-set versions, driven through the
/// REAL endpoint handlers (<c>CommandEndpoints.SubmitAsync</c>, <c>SyncContractFilter</c>) — no internals re-implemented.
/// Wire: <c>WireContract.SupportedContractVersions = {1,2}</c>; commands accept both, sync DTOs are v2-only and answer a
/// v1/unknown client with 426 + <c>X-Animora-Supported-Versions</c> so it can resync/upgrade instead of mis-mapping.
/// Rule set: <see cref="RuleSetPolicy"/> (<c>Current</c>, <c>SupportedFrom</c>).
/// </summary>
public sealed class MixedFleetTests
{
    private static DefaultHttpContext Request(CommandEnvelope envelope)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        return context;
    }

    private static async Task<(int Status, SettlementResult? Settlement, DefaultHttpContext Context)> SubmitAsync(IntakeTestFixture f, CommandEnvelope envelope)
    {
        DefaultHttpContext context = Request(envelope);
        IResult result = await CommandEndpoints.SubmitAsync(context, f.CreateService());
        int status = (result as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;
        return (status, (result as IValueHttpResult)?.Value as SettlementResult, context);
    }

    /// <summary>A real in-memory host with the production sync contract filter in front of a trivial endpoint.</summary>
    private static Task<WebApplication> SyncHostAsync() => TestHostFactory.StartAsync(
        "Development",
        _ => { },
        app =>
        {
            RouteGroupBuilder group = app.MapGroup("/sync").AddEndpointFilter<SyncContractFilter>();
            group.MapGet("/manifest", () => Results.Ok(new { ok = true }));
            group.MapPost("/batches", () => Results.Ok(new { ok = true }));
        });

    [Fact]
    public void WireContract_SupportsExactlyN_And_NMinus1()
    {
        Assert.Equal([1, 2], WireContract.SupportedContractVersions);
        Assert.True(WireContract.IsSupported(1));
        Assert.True(WireContract.IsSupported(2));
        Assert.False(WireContract.IsSupported(0));
        Assert.False(WireContract.IsSupported(3));
        Assert.Equal(2, WireContract.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData("3")]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("2,2")]
    public async Task V1Client_CallsSync_Gets426_WithSupportedVersions(string? header)
    {
        await using WebApplication app = await SyncHostAsync();
        using HttpClient client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/sync/manifest");
        if (header is not null)
        {
            request.Headers.TryAddWithoutValidation(WireContract.HeaderName, header);
        }

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal((HttpStatusCode)426, response.StatusCode);
        Assert.Equal("1,2", Assert.Single(response.Headers.GetValues(WireContract.SupportedVersionsHeaderName)));
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CONTRACT_UNSUPPORTED", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task V2Client_CallsSync_IsAccepted_OnEveryRoute()
    {
        await using WebApplication app = await SyncHostAsync();
        using HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(WireContract.HeaderName, "2");

        using HttpResponseMessage manifest = await client.GetAsync("/sync/manifest");
        using HttpResponseMessage batches = await client.PostAsync("/sync/batches", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        Assert.Equal(HttpStatusCode.OK, batches.StatusCode);
    }

    [Fact]
    public async Task V2Command_Accepted()
    {
        var f = new IntakeTestFixture();
        (int status, SettlementResult? settlement, _) = await SubmitAsync(f, f.Envelope() with { ContractVersion = 2 });

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(SettlementOutcome.Accepted, settlement!.Outcome);
        Assert.Equal(1, f.Dispatcher.Calls);
    }

    [Fact]
    public async Task V1Command_StillAccepted()
    {
        var f = new IntakeTestFixture();
        (int status, SettlementResult? settlement, _) = await SubmitAsync(f, f.Envelope() with { ContractVersion = 1 });

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(SettlementOutcome.Accepted, settlement!.Outcome);
        Assert.Equal(1, f.Dispatcher.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(99)]
    public async Task UnknownVersion_Rejected_426(int version)
    {
        var f = new IntakeTestFixture();
        (int status, SettlementResult? settlement, DefaultHttpContext context) = await SubmitAsync(f, f.Envelope() with { ContractVersion = version });

        Assert.Equal(StatusCodes.Status426UpgradeRequired, status);
        Assert.Null(settlement);
        Assert.Equal("1,2", context.Response.Headers["X-Animora-Supported-Versions"].ToString());
        Assert.Equal(0, f.Dispatcher.Calls);
        Assert.Equal(0, f.Store.BeginCalls);
    }

    [Fact]
    public async Task StaleRuleSetVersion_BelowSupportedFrom_RejectedWithReason()
    {
        var f = new IntakeTestFixture { Rules = new RuleSetPolicy { Current = 3, SupportedFrom = 2 } };
        (_, SettlementResult? settlement, _) = await SubmitAsync(f, f.Envelope() with { RuleSetVersion = "1" });

        Assert.Equal(SettlementOutcome.Rejected, settlement!.Outcome);
        Assert.Equal(ErrorCodes.RuleSetUnsupported.Code, settlement.ErrorCode);
        Assert.Equal(0, f.Dispatcher.Calls);
    }

    [Fact]
    public async Task RuleSetVersion_Between_ServerReDerives_NotForceApplied()
    {
        var f = new IntakeTestFixture { Rules = new RuleSetPolicy { Current = 3, SupportedFrom = 2 } };

        // An older-but-supported client (v2 while the server is on v3) is evaluated under ITS rule set: the receipt records
        // ruleset=2 (re-derived server-side for that version), never silently rewritten to the current ruleset=3.
        (_, SettlementResult? between, _) = await SubmitAsync(f, f.Envelope() with { RuleSetVersion = "2" });
        Assert.Equal(SettlementOutcome.Accepted, between!.Outcome);
        Assert.Contains("ruleset=2", between.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("ruleset=3", between.Detail, StringComparison.Ordinal);

        // Absent ⇒ current; a client AHEAD of the server is not accepted either.
        (_, SettlementResult? absent, _) = await SubmitAsync(f, f.Envelope());
        Assert.Contains("ruleset=3", absent!.Detail, StringComparison.Ordinal);

        (_, SettlementResult? ahead, _) = await SubmitAsync(f, f.Envelope() with { RuleSetVersion = "4" });
        Assert.Equal(ErrorCodes.RuleSetUnsupported.Code, ahead!.ErrorCode);
    }
}
