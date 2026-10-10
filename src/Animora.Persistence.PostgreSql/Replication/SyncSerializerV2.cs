using System.Text.Json;
namespace Animora.Persistence.PostgreSql.Replication;

public static class SyncSerializerV2
{
    public static JsonSerializerOptions Options { get; } = Create();
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DictionaryKeyPolicy = JsonNamingPolicy.CamelCase };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
