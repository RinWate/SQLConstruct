using System.Text.Json;
using System.Text.Json.Serialization;
using SQLConstruct.Models;

namespace SQLConstruct.Services;

/// <summary>Сохранение/загрузка схемы базы в JSON.</summary>
public static class JsonSchemaStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static DatabaseSchema? Load(string path) =>
        JsonSerializer.Deserialize<DatabaseSchema>(File.ReadAllText(path), Options);

    public static void Save(string path, DatabaseSchema schema) =>
        File.WriteAllText(path, JsonSerializer.Serialize(schema, Options));
}
