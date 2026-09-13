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

        var variables = new XElement("localVars", program.Variables.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v =>
            new XElement("variable", new XAttribute("name", v.Key), new XElement("type", new XElement(TypeName(v.DataType))))));
        var pou = new XElement("pou", new XAttribute("name", "AtlasProgram"), new XAttribute("pouType", "program"),
            new XElement("interface", variables), new XElement("body", new XElement("ST", new XCData(st.Source))));
        XNamespace plc = "http://www.plcopen.org/xml/tc6_0201";
        var project = new XElement(plc + "project",
            new XElement("fileHeader", new XAttribute("companyName", "AtlasSoftPlc"), new XAttribute("productName", "AtlasPLC"), new XAttribute("productVersion", "1")),
            new XElement("contentHeader", new XAttribute("name", program.Name), new XAttribute("version", program.Version), new XAttribute("modificationDateTime", "1970-01-01T00:00:00Z")),
            new XElement("types", new XElement("pous", pou)), new XElement("instances", new XElement("configurations")));
        var document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), project);

        var settings = new System.Xml.XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, OmitXmlDeclaration = false, NewLineChars = "\n", NewLineHandling = System.Xml.NewLineHandling.Replace };
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        using (var xmlWriter = System.Xml.XmlWriter.Create(writer, settings)) document.Save(xmlWriter);
        var xml = writer.ToString().Replace("\r\n", "\n");
        return new PlcOpenXmlArtifact { Xml = xml, Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml))).ToLowerInvariant() };
    }

    private static XName TypeName(Domain.Common.PlcDataType type) => type switch
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
