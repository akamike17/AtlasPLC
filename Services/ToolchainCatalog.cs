using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Web.Services;

/// <summary>Catálogo de toolchains: describe capacidades reales, no afirma instalaciones inexistentes.</summary>
public sealed class CatalogToolchainPlugin(ToolchainDescriptor descriptor, string state, string detail) : IToolchainPlugin
{
    public ToolchainDescriptor Descriptor { get; } = descriptor;

    public Task<ToolchainStatus> GetStatusAsync(CancellationToken ct = default) => Task.FromResult(new ToolchainStatus(
        state,
        detail,
        Descriptor.Detected || state is "Ready" or "Detected",
        Descriptor.Configured || state is "Ready" or "Configured",
        Descriptor.CanCompile,
        Descriptor.CanDeploy));
}

public static class ToolchainCatalog
{
    public static void AddBuiltIns(IServiceCollection services)
    {
        Add(services, new ToolchainDescriptor { Id = "atlas-iec-st", TargetPluginId = "iec-st", DisplayName = "Atlas IEC Structured Text exporter", Vendor = "Atlas", Version = "1", Mode = ToolchainMode.ExportOnly, Detected = true, Configured = true }, "Ready", "Generador determinista integrado; exporta ST, no compila ni despliega.");
        Add(services, new ToolchainDescriptor { Id = "atlas-plcopen", TargetPluginId = "plcopen-xml", DisplayName = "Atlas PLCopen XML exporter", Vendor = "Atlas", Version = "1", Mode = ToolchainMode.ExportOnly, Detected = true, Configured = true }, "Partial", "Exportador PLCopen XML integrado; requiere validación posterior en el IDE del proveedor.");
        Add(services, new ToolchainDescriptor { Id = "openplc", TargetPluginId = "openplc", DisplayName = "OpenPLC Runtime", Vendor = "OpenPLC", Mode = ToolchainMode.Assisted }, "NotConfigured", "Configura el endpoint y verifica la API HTTP antes de operar.");
        Add(services, new ToolchainDescriptor { Id = "siemens-tia", TargetPluginId = "siemens-s7", DisplayName = "Siemens TIA Portal", Vendor = "Siemens", Mode = ToolchainMode.Unavailable }, "Unavailable", "No instalado ni verificado en este equipo.");
        Add(services, new ToolchainDescriptor { Id = "rockwell-studio5000", TargetPluginId = "rockwell-logix", DisplayName = "Rockwell Studio 5000", Vendor = "Rockwell", Mode = ToolchainMode.Unavailable }, "Unavailable", "No instalado ni verificado en este equipo.");
        Add(services, new ToolchainDescriptor { Id = "mitsubishi-gxworks", TargetPluginId = "mitsubishi-melsec", DisplayName = "Mitsubishi GX Works", Vendor = "Mitsubishi", Mode = ToolchainMode.Unavailable }, "Unavailable", "No instalado ni verificado en este equipo.");
        Add(services, new ToolchainDescriptor { Id = "omron-sysmac-studio", TargetPluginId = "omron-sysmac", DisplayName = "Omron Sysmac Studio", Vendor = "Omron", Mode = ToolchainMode.Unavailable }, "Unavailable", "No instalado ni verificado en este equipo.");
        Add(services, new ToolchainDescriptor { Id = "beckhoff-twincat", TargetPluginId = "beckhoff-twincat", DisplayName = "Beckhoff TwinCAT", Vendor = "Beckhoff", Mode = ToolchainMode.Unavailable }, "Unavailable", "No instalado ni verificado en este equipo.");
    }

    private static void Add(IServiceCollection services, ToolchainDescriptor descriptor, string state, string detail) =>
        services.AddSingleton<IToolchainPlugin>(_ => new CatalogToolchainPlugin(descriptor, state, detail));
}
