using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AtlasSoftPlc.Domain.Projects;

namespace AtlasSoftPlc.Application.Backends;

public sealed class PlcOpenXmlArtifact
{
    public string Xml { get; init; } = string.Empty;
    public string Hash { get; init; } = string.Empty;
    public IReadOnlyList<EmitterDiagnostic> Diagnostics { get; init; } = Array.Empty<EmitterDiagnostic>();
    public bool IsSupported => Diagnostics.Count == 0;
    public string Status { get; init; } = "PLCopen XML candidate";
}

/// <summary>Empaqueta el ST ya validado en un PLCopen XML mínimo y determinista.</summary>
public sealed class PlcOpenXmlEmitter
{
    private readonly StructuredTextEmitter _st = new();

    public PlcOpenXmlArtifact Emit(PlcProgramDefinition program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var st = _st.Emit(program);
        if (!st.IsSupported)
            return new PlcOpenXmlArtifact { Diagnostics = st.Diagnostics };

        XNamespace plc = "http://www.plcopen.org/xml/tc6_0201";
        var variables = new XElement(plc + "localVars", program.Variables.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v =>
            new XElement(plc + "variable", new XAttribute("name", v.Key), new XElement(plc + "type", new XElement(plc + TypeName(v.DataType))))));
        var pou = new XElement(plc + "pou", new XAttribute("name", "AtlasProgram"), new XAttribute("pouType", "program"),
            new XElement(plc + "interface", variables), new XElement(plc + "body", new XElement(plc + "ST", new XCData(st.Source))));
        var project = new XElement(plc + "project",
            new XElement(plc + "fileHeader", new XAttribute("companyName", "AtlasSoftPlc"), new XAttribute("productName", "AtlasPLC"), new XAttribute("productVersion", "1")),
            new XElement(plc + "contentHeader", new XAttribute("name", program.Name), new XAttribute("version", program.Version), new XAttribute("modificationDateTime", "1970-01-01T00:00:00Z")),
            new XElement(plc + "types", new XElement(plc + "pous", pou)), new XElement(plc + "instances", new XElement(plc + "configurations")));
        var document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), project);

        var settings = new System.Xml.XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, OmitXmlDeclaration = false, NewLineChars = "\n", NewLineHandling = System.Xml.NewLineHandling.Replace };
        using var stream = new MemoryStream();
        using (var xmlWriter = System.Xml.XmlWriter.Create(stream, settings)) document.Save(xmlWriter);
        var bytes = stream.ToArray();
        var xml = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n");
        return new PlcOpenXmlArtifact { Xml = xml, Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml))).ToLowerInvariant() };
    }

    private static string TypeName(Domain.Common.PlcDataType type) => type switch
    {
        Domain.Common.PlcDataType.Bool => "BOOL",
        Domain.Common.PlcDataType.Int16 => "INT",
        Domain.Common.PlcDataType.UInt16 => "UINT",
        Domain.Common.PlcDataType.Int32 => "DINT",
        Domain.Common.PlcDataType.UInt32 => "UDINT",
        Domain.Common.PlcDataType.Float => "REAL",
        Domain.Common.PlcDataType.Double => "LREAL",
        _ => "BOOL"
    };
}
