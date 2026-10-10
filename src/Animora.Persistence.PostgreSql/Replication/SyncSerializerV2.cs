using System.Text.Json;
namespace Animora.Persistence.PostgreSql.Replication;

public static class SyncSerializerV2
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
