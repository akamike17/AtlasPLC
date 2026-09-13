using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Hosting;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Runtime.Targets;

/// <summary>
/// Target "Atlas Runtime" (spec §24/§39 FASE B): envuelve el runtime de simulación
/// existente SIN duplicarlo. El runtime actual es la fuente de verdad de la
/// simulación; este adapter lo expone como un target más del workbench.
///
/// Capacidades declaradas (honestas, P0-4):
///  - Simulate (motor de simulación local) → implementado vía <see cref="SimulateAsync"/>.
///  - ReadLiveData / WriteLiveData / ReadSymbols (estado online de entradas/salidas).
///  - Generate/Compile/Deploy/Verify NO: el runtime no genera ni despliega artefactos.
///
/// Regla de contrato: <see cref="GenerateAsync"/> queda Unsupported (no declara
/// Generate); la simulación se instala SOLO por <see cref="SimulateAsync"/>.
/// </summary>
public sealed class AtlasRuntimeTargetAdapter : PlcTargetAdapterBase
{
    private readonly PlcRuntimeService _runtime;

    public AtlasRuntimeTargetAdapter(PlcRuntimeService runtime)
        : base(
            new TargetIdentity { Manufacturer = "AtlasSoftPlc", Family = "AtlasRuntime", Model = "Simulation" },
            new[]
            {
                TargetCapability.Simulate,
                TargetCapability.ReadLiveData,
                TargetCapability.WriteLiveData,
                TargetCapability.ReadSymbols,
            })
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    /// <summary>
    /// Instala y arranca un proyecto en el runtime de simulación local. Esta es la
    /// implementación de la capacidad <c>Simulate</c> — NO reutiliza Generate.
    /// </summary>
    public override async Task<TargetOperationResult> SimulateAsync(PlcProgramDefinition project, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var replaced = await _runtime.ReplaceProgramAsync(project, autoStart: true, ct).ConfigureAwait(false);
            return replaced
                ? TargetOperationResult.Ok(detail: "Proyecto instalado transaccionalmente en el runtime de simulación local.")
                : TargetOperationResult.Fail("El runtime rechazó el reemplazo del programa.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return TargetOperationResult.Fail($"No se pudo instalar la simulación: {ex.Message}");
        }
    }

    public override TargetProfile Profile => new()
    {
        Identity = Identity, Capabilities = Capabilities,
        SupportLevel = TargetSupportLevel.L1_Monitor,
        DefaultConnection = new TargetConnectionProfile
        {
            ConnectionType = TargetConnectionType.Simulator,
            Transport = TargetTransport.Ethernet,
            Address = "127.0.0.1",
            Protocol = "AtlasRuntime/Internal"
        }
    };

    /// <summary>Lee el estado online de las salidas del runtime (snapshot inmutable).</summary>
    public Task<IReadOnlyDictionary<Guid, RuntimeValue>> ReadLiveOutputsAsync() =>
        Task.FromResult(_runtime.GetOutputs());
}
