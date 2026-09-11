using System.Text.Json;
using System.Text.Json.Serialization;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Common;

namespace AtlasSoftPlc.Infrastructure.Persistence;

/// <summary>
/// Serialización JSON del modelo de lógica (IR polimórfico).
/// Usa los JsonDerivedType attributes definidos en el Domain.
/// </summary>
public static class AtlasJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}