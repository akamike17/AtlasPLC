using AtlasSoftPlc.Domain.Devices;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Protocols.Modbus.Targets;

/// <summary>
/// Adapter conceptual que demuestra la reclasificación de Modbus (spec §14.8/§41):
/// Modbus NO programa PLCs. Es un <b>adapter de datos online / I/O remoto</b>, útil
/// para commissioning, protocol testing, digital twins y monitor online.
///
/// Por diseño declara SOLO capacidades de monitor/escritura de datos:
///  - ReadLiveData / WriteLiveData / ReadSymbols / Discover
/// NO declara Generate/Compile/Deploy/Verify: un dispositivo Modbus no es un
/// target de despliegue de proyecto. Cualquier operación de ingeniería devuelve
/// "Unsupported" honestamente.
/// </summary>
public sealed class ModbusOnlineAdapter : PlcTargetAdapterBase
{
    public ModbusOnlineAdapter()
        : base(
            new TargetIdentity { Manufacturer = "Generic", Family = "Modbus", Model = "TCP" },
            new[]
            {
                TargetCapability.Discover,
                TargetCapability.ReadLiveData,
                TargetCapability.WriteLiveData,
                TargetCapability.ReadSymbols,
            })
    {
    }

    /// <summary>
    /// Prueba de contrato clave: si el proyecto exige capacidad de generación,
    /// este target queda BLOQUEADO (no puede programar un dispositivo Modbus).
    /// </summary>
    public override Task<CompatibilityReport> ValidateAsync(PlcProgramDefinition project, CancellationToken ct = default)
    {
        // Un adapter de datos online NUNCA genera un artefacto de programa.
        return Task.FromResult(new CompatibilityReport
        {
            Status = CompatibilityReport.CompatibilityStatus.Blocked,
            Messages = new[]
            {
                "Modbus es un adapter de datos online / I/O remoto, no un target de despliegue de proyecto.",
                "No se puede generar ni desplegar un programa PLC hacia un dispositivo Modbus genérico.",
            },
        });
    }

    /// <summary>
    /// Los proyectos no son "programables" en Modbus; Generate es siempre no soportado.
    /// </summary>
    public override Task<TargetOperationResult> GenerateAsync(PlcProgramDefinition project, CancellationToken ct = default) =>
        Task.FromResult(TargetOperationResult.Unsupported("Generate"));
}