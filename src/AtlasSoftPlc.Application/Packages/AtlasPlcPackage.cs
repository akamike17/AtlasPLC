using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Common;
using System.Text.Json.Serialization;

namespace AtlasSoftPlc.Application.Packages;

public sealed record AtlasPlcPackageManifest(int FormatVersion, string ProjectId, string ProjectHash);
public sealed class AtlasPlcPackageContent
{
    public AtlasIrDocument Ir { get; init; } = new();
    public IReadOnlyDictionary<string, byte[]> Artifacts { get; init; } = new Dictionary<string, byte[]>();
    public IReadOnlyDictionary<string, byte[]> Scenarios { get; init; } = new Dictionary<string, byte[]>();
    public IReadOnlyDictionary<string, byte[]> Targets { get; init; } = new Dictionary<string, byte[]>();
    public IReadOnlyDictionary<string, byte[]> Traces { get; init; } = new Dictionary<string, byte[]>();
}

/// <summary>Contenedor versionado sin secretos; el contenido semántico es IR + Plant Model.</summary>
public sealed class AtlasPlcPackageService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, Converters = { new JsonStringEnumConverter(), new PlcValueJsonConverter() } };

    public byte[] Export(AtlasIrDocument ir, IReadOnlyDictionary<string, byte[]>? artifacts = null,
        IReadOnlyDictionary<string, byte[]>? scenarios = null, IReadOnlyDictionary<string, byte[]>? targets = null,
        IReadOnlyDictionary<string, byte[]>? traces = null)
    {
        ArgumentNullException.ThrowIfNull(ir);
        var program = ir.ToProgramDefinition();
        program.Hash = CanonicalProgramHasher.ComputeHash(program);
        var manifest = new AtlasPlcPackageManifest(1, ir.Id.ToString("D"), program.Hash);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            AddJson(zip, "manifest.json", manifest);
            AddJson(zip, "ir.json", ir);
            AddJson(zip, "plant.json", ir.Plant);
            AddDirectory(zip, "scenarios", scenarios);
            AddDirectory(zip, "targets", targets);
            AddDirectory(zip, "artifacts", artifacts);
            AddDirectory(zip, "traces", traces);
        }
        return output.ToArray();
    }

    public AtlasPlcPackageContent Import(byte[] package)
    {
        using var input = new MemoryStream(package ?? throw new ArgumentNullException(nameof(package)));
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        var manifest = ReadJson<AtlasPlcPackageManifest>(zip, "manifest.json");
        var ir = ReadJson<AtlasIrDocument>(zip, "ir.json");
        if (manifest.FormatVersion != 1 || !Guid.TryParse(manifest.ProjectId, out var id) || id != ir.Id)
            throw new InvalidDataException("El paquete AtlasPLC tiene un manifest incompatible.");
        var hash = CanonicalProgramHasher.ComputeHash(ir.ToProgramDefinition());
        if (!string.Equals(hash, manifest.ProjectHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("El hash del proyecto no coincide con el manifest.");
        return new AtlasPlcPackageContent { Ir = ir, Scenarios = ReadDirectory(zip, "scenarios"), Targets = ReadDirectory(zip, "targets"), Artifacts = ReadDirectory(zip, "artifacts"), Traces = ReadDirectory(zip, "traces") };
    }

    private static void AddJson<T>(ZipArchive zip, string name, T value)
    { var entry = zip.CreateEntry(name, CompressionLevel.Optimal); using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)); writer.Write(JsonSerializer.Serialize(value, Json)); }
    private static void AddDirectory(ZipArchive zip, string directory, IReadOnlyDictionary<string, byte[]>? files)
    {
        if (files is null || files.Count == 0) { zip.CreateEntry(directory + "/.keep"); return; }
        foreach (var file in files.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var safe = file.Key.Replace('\\', '/');
            if (safe.StartsWith('/') || safe.Contains("../", StringComparison.Ordinal) || safe.Contains("..\\", StringComparison.Ordinal)) throw new InvalidDataException("Ruta de paquete inválida.");
            var entry = zip.CreateEntry(directory + "/" + safe, CompressionLevel.Optimal); using var stream = entry.Open(); stream.Write(file.Value);
        }
    }
    private static T ReadJson<T>(ZipArchive zip, string name) { var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"Falta {name}."); using var reader = new StreamReader(entry.Open(), Encoding.UTF8); return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), Json) ?? throw new InvalidDataException($"JSON inválido: {name}."); }
    private static IReadOnlyDictionary<string, byte[]> ReadDirectory(ZipArchive zip, string directory) => zip.Entries.Where(e => e.FullName.StartsWith(directory + "/", StringComparison.Ordinal) && !e.FullName.EndsWith("/.keep", StringComparison.Ordinal)).ToDictionary(e => e.FullName[(directory.Length + 1)..], e => ReadBytes(e), StringComparer.Ordinal);
    private static byte[] ReadBytes(ZipArchiveEntry entry) { using var stream = entry.Open(); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }
}

internal sealed class PlcValueJsonConverter : JsonConverter<PlcValue>
{
    public override PlcValue Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var typeName = root.GetProperty("dataType").GetString() ?? nameof(PlcDataType.Bool);
        var dataType = Enum.Parse<PlcDataType>(typeName, true);
        if (!root.TryGetProperty("value", out var value) || value.ValueKind == JsonValueKind.Null) return PlcValue.Null(dataType);
        object raw = dataType switch
        {
            PlcDataType.Bool => value.GetBoolean(), PlcDataType.Int16 => value.GetInt16(), PlcDataType.UInt16 => value.GetUInt16(),
            PlcDataType.Int32 => value.GetInt32(), PlcDataType.UInt32 => value.GetUInt32(), PlcDataType.Int64 => value.GetInt64(),
            PlcDataType.UInt64 => value.GetUInt64(), PlcDataType.Float => value.GetSingle(), PlcDataType.Double => value.GetDouble(),
            PlcDataType.Decimal => value.GetDecimal(), PlcDataType.String => value.GetString() ?? string.Empty,
            _ => value.GetString() ?? string.Empty
        };
        return new PlcValue(dataType, raw);
    }
    public override void Write(Utf8JsonWriter writer, PlcValue value, JsonSerializerOptions options)
    {
        writer.WriteStartObject(); writer.WriteString("dataType", value.DataType.ToString());
        if (value.HasValue) writer.WritePropertyName("value"); else writer.WriteNull("value");
        if (value.HasValue) JsonSerializer.Serialize(writer, value.Raw, value.Raw?.GetType() ?? typeof(object), options);
        writer.WriteEndObject();
    }
}
