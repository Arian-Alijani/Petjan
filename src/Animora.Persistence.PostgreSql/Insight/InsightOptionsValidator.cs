using Microsoft.Extensions.Options;
namespace Animora.Persistence.PostgreSql.Insight;
public sealed class InsightOptionsValidator : IValidateOptions<InsightOptions>
{
    public ValidateOptionsResult Validate(string? name, InsightOptions options)
    {
        if (options.Enabled && string.IsNullOrWhiteSpace(options.ConsentAdrReference))
            return ValidateOptionsResult.Fail("Insight requires an explicit consent ADR reference.");
        if (options.CacheTtlSeconds is < 1 or > 3600)
            return ValidateOptionsResult.Fail("Insight cache TTL must be between 1 and 3600 seconds.");
        return ValidateOptionsResult.Success;
    }
}
