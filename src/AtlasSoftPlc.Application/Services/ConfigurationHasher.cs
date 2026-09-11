using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AtlasSoftPlc.Domain.Devices;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Variables;

namespace AtlasSoftPlc.Application.Services;

/// <summary>Calcula el SHA-256 de la configuración completa (sección 29).</summary>
public sealed class ConfigurationHasher
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null
    };

    public string ComputeHash(
        LogicProgram program,
        IReadOnlyCollection<VariableDefinition> variables,
        IReadOnlyCollection<TagBinding> bindings,
        IReadOnlyCollection<DeviceDefinition> devices)
    {
        var sb = new StringBuilder();
        sb.Append(JsonSerializer.Serialize(program.OrderedForHash(), JsonOpts));
        sb.Append(JsonSerializer.Serialize(variables.OrderBy(v => v.Id), JsonOpts));
        sb.Append(JsonSerializer.Serialize(bindings.OrderBy(b => b.Id), JsonOpts));
        sb.Append(JsonSerializer.Serialize(devices.OrderBy(d => d.Id), JsonOpts));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

internal static class HashExtensions
{
    public static object OrderedForHash(this LogicProgram program) => new
    {
        program.Id,
        program.Name,
        program.Version,
        Rules = program.Rules.OrderBy(r => r.Id).Select(r => new
        {
            r.Id,
            r.Name,
            r.Priority,
            r.Enabled,
            ConditionType = r.Condition?.GetType().Name
        })
    };
}