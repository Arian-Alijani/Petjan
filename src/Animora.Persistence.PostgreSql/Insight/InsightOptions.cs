namespace Animora.Persistence.PostgreSql.Insight;
public sealed class InsightOptions
{
    public const string SectionName = "Insight";
    public bool Enabled { get; set; }
    public string? ConsentAdrReference { get; set; }
    public int CacheTtlSeconds { get; set; } = 60;
}
