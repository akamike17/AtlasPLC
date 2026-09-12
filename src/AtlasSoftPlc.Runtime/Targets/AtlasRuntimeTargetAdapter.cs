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
/// Capacidades declaradas (honestas):
///  - Simulate (motor de simulación local)
///  - ReadLiveData / WriteLiveData (estado online de entradas/salidas)
///  - Compile/Generate NO: el runtime no genera artefactos de terceros.
///  - Deploy/Verify NO: no hay PLC físico.
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

    /// <summary>Instala y arranca un proyecto en el runtime de simulación local.</summary>
    public override Task<TargetOperationResult> GenerateAsync(PlcProgramDefinition project, CancellationToken ct = default)
    {
        // "Generar" contra el runtime local = instalar configuración y arrancar.
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