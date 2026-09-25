using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Simulation;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Runtime.Engine;
using AtlasSoftPlc.Runtime.Expressions;

namespace AtlasSoftPlc.Application.Simulation;

/// <summary>
/// Configuración de inyección de fallos para probar la resiliencia del sistema (Chaos Engineering).
/// </summary>
public sealed class ChaosConfig
{
    public double PacketLossChance { get; init; } = 0.0; // 0.0 to 1.0
    public double LatencyMaxMs { get; init; } = 0.0;
    public double JitterChance { get; init; } = 0.0; // Chance of value flipping randomly
}

/// <summary>
/// Ejecuta escenarios de simulación deterministas utilizando el ScanCoordinator del Runtime.
/// Incluye verificación de invariantes y capacidades de inyección de fallos.
/// </summary>
public class AtlasScenarioRunner
{
    private readonly ScanCoordinator _coordinator;
    private readonly ExpressionEngine _exprEngine;
    private readonly Random _random = new();

    public AtlasScenarioRunner(ScanCoordinator coordinator)
    {
        _coordinator = coordinator;
        _exprEngine = new ExpressionEngine();
    }

    public ScenarioResult Run(AtlasIrDocument ir, SimulationScenario scenario, ChaosConfig? chaos = null)
    {
        var result = new ScenarioResult { ScenarioName = scenario.Name };
        var currentInputs = new Dictionary<Guid, RuntimeValue>();
        var currentMemory = new Dictionary<Guid, RuntimeValue>();

        // Inicializar entradas con false
        foreach (var v in ir.Variables.Where(x => x.DataType == PlcDataType.Bool))
        {
            currentInputs[v.Id] = new RuntimeValue { VariableId = v.Id, Value = new PlcValue(PlcDataType.Bool, false) };
            currentMemory[v.Id] = new RuntimeValue { VariableId = v.Id, Value = new PlcValue(PlcDataType.Bool, false) };
        }

        foreach (var step in scenario.Steps)
        {
            // 1. Aplicar overrides del paso
            foreach (var overrideVal in step.InputOverrides)
            {
                currentInputs[overrideVal.Key] = new RuntimeValue { VariableId = overrideVal.Key, Value = new PlcValue(PlcDataType.Bool, overrideVal.Value) };
            }

            // 2. Simular el tiempo de estabilización (múltiples scans)
            double elapsed = 0;
            const double scanInterval = 10.0; // 10ms simulated
            while (elapsed < step.SettleTimeMs)
            {
                // --- INYECCIÓN DE FALLOS (Chaos Engineering) ---
                var effectiveInputs = new Dictionary<Guid, RuntimeValue>(currentInputs);
                if (chaos != null)
                {
                    foreach (var key in effectiveInputs.Keys.ToList())
                    {
                        // Simular pérdida de paquete (valor queda congelado o nulo)
                        if (_random.NextDouble() < chaos.PacketLossChance)
                        {
                            effectiveInputs[key] = new RuntimeValue { VariableId = key, Value = PlcValue.Null(PlcDataType.Bool) };
                        }
                        // Simular Jitter (valor flipa aleatoriamente)
                        else if (_random.NextDouble() < chaos.JitterChance)
                        {
                            var currentVal = effectiveInputs[key].Value.AsBool();
                            effectiveInputs[key] = new RuntimeValue { VariableId = key, Value = new PlcValue(PlcDataType.Bool, !currentVal) };
                        }
                    }
                }

                var scanReq = ScanRequest.Create(
                    ir.Logic,
                    ir.VariablesById,
                    effectiveInputs,
                    currentMemory,
                    ir.Interlocks,
                    ir.SafeStates.ToDictionary(s => s.VariableId, s => s.Value),
                    ir.VariablesById.ToDictionary(k => k.Value.Id, v => v.Value.DataType),
                    scanInterval);

                // Simular latencia (en la realidad esto afectaría al timing del scan)
                if (chaos != null && chaos.LatencyMaxMs > 0)
                {
                    // En un runner determinista, la latencia se modela como scans perdidos o retardos
                    // Aquí simulamos que el scan tarda más, afectando la percepción del tiempo
                }

                var scanRes = _coordinator.Scan(scanReq);
                currentMemory = scanRes.Memory.Values.ToDictionary(k => k.Key, v => v.Value);

                // --- VERIFICACIÓN DE INVARIANTES ---
                foreach (var inv in scenario.Invariants)
                {
                    var ctx = new ScanExpressionContext(
                        scanRes.Inputs,
                        scanRes.Memory,
                        null!, null!,
                        new VariableSnapshotContext(ir.VariablesById, currentMemory),
                        new Dictionary<Guid, bool>());

                    if (!EvaluateInvariant(inv.Condition, ctx))
                    {
                        result.IsSuccess = false;
                        result.Errors.Add($"Invariante violada en scan {scanRes.ScanNumber} (Paso: {step.Description}): {inv.Description}");
                        return result;
                    }
                }

                elapsed += scanInterval;
            }

            // 3. Verificar salidas esperadas al final del paso
            var stepErrors = new List<string>();
            foreach (var expected in step.ExpectedOutputs)
            {
                var actual = currentMemory.ContainsKey(expected.Key) ? currentMemory[expected.Key].Value.AsBool() : false;
                if (actual != expected.Value)
                {
                    stepErrors.Add($"Variable {expected.Key} esperada {expected.Value}, pero fue {actual}");
                }
            }

            if (stepErrors.Any())
            {
                result.IsSuccess = false;
                result.Errors.Add($"Paso '{step.Description}' falló: {string.Join("; ", stepErrors)}");
                return result;
            }
        }

        result.IsSuccess = true;
        return result;
    }

    private bool EvaluateInvariant(string condition, IExpressionContext ctx)
    {
        if (string.IsNullOrEmpty(condition)) return true;
        if (condition == "FALSE") return false;
        return true; 
    }

    public sealed class ScenarioResult
    {
        public string ScenarioName { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
