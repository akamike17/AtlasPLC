using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
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
    public override Task<TargetOperationResult> SimulateAsync(PlcProgramDefinition project, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var variables = project.Variables.ToDictionary(v => v.Id);
        var failsafe = project.Failsafe ?? new Dictionary<Guid, PlcValue>();
        _runtime.InstallConfiguration(project.Logic, variables, new List<Interlock>(), failsafe);
        _runtime.Post(new ResumeCommand());

        return Task.FromResult(TargetOperationResult.Ok(detail: "Proyecto instalado en el runtime de simulación local."));
    }

    /// <summary>Lee el estado online de las salidas del runtime (snapshot inmutable).</summary>
    public Task<IReadOnlyDictionary<Guid, RuntimeValue>> ReadLiveOutputsAsync() =>
        Task.FromResult(_runtime.GetOutputs());
}