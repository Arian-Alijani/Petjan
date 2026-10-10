using Animora.Domain.Results;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
using Animora.Persistence.PostgreSql.Insight;
using Microsoft.Extensions.Options;
namespace Animora.Server.Admin;
internal sealed class AdminInsightStatusAdapter(IOptions<InsightOptions> options, AdminSession session) : IAdminInsightStatus
{
    public Task<Result<InsightStatus>> GetAsync(CancellationToken ct = default)
    {
        _ = session.OperatorId; ct.ThrowIfCancellationRequested();
        return Task.FromResult(Result<InsightStatus>.Ok(new(options.Value.Enabled, !string.IsNullOrWhiteSpace(options.Value.ConsentAdrReference))));
    }
}
internal sealed class InsightOperatorContext(AdminSession session) : IInsightOperatorContext { public string OperatorId => session.OperatorId; }
