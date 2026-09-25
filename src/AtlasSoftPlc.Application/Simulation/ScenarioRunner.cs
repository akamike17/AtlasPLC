using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Simulation;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Runtime.Engine;
using AtlasSoftPlc.Runtime.Expressions;
using AtlasSoftPlc.Domain.Logic;

namespace AtlasSoftPlc.Application.Simulation;

/// <summary>
/// Configuración de inyección de fallos para probar la resiliencia del sistema (Chaos Engineering).
/// </summary>
public sealed class ChaosConfig
{
    public double PacketLossChance { get; init; } = 0.0; // 0.0 to 1.0
    public double LatencyMaxMs { get; init; } = 0.0;
    public double JitterChance { get; init; } = 0.0; // Chance of value flipping randomly
    public int? Seed { get; init; } = null; // For determinism/replayability
}

/// <summary>
/// Ejecuta escenarios de simulación deterministas utilizando el ScanCoordinator del Runtime.
/// Incluye verificación de invariantes y capacidades de inyección de fallos.
/// </summary>
public class AtlasScenarioRunner
{
    private readonly ScanCoordinator _coordinator;
    private readonly ExpressionEngine _exprEngine;

    public AtlasScenarioRunner(ScanCoordinator coordinator)
    {
        _coordinator = coordinator;
        _exprEngine = new ExpressionEngine();
    }

    public ScenarioResult Run(AtlasIrDocument ir, SimulationScenario scenario, ChaosConfig? chaos = null)
    {
        var random = chaos?.Seed != null ? new Random(chaos.Seed.Value) : new Random();
        var result = new ScenarioResult { ScenarioName = scenario.Name };
        var currentInputs = new Dictionary<Guid, RuntimeValue>();
        var currentMemory = new Dictionary<Guid, RuntimeValue>();
        var latencyQueue = new Queue<(double releaseTime, Dictionary<Guid, RuntimeValue> values)>();

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
            do
            {
                // --- IMPLEMENTACIÓN DE LATENCIA REAL ---
                // Si hay latencia, los inputs actuales no se aplican inmediatamente, se encolan.
                double currentLatency = 0;
                if (chaos != null && chaos.LatencyMaxMs > 0)
                {
                    currentLatency = random.NextDouble() * chaos.LatencyMaxMs;
                }

                var valuesToApply = new Dictionary<Guid, RuntimeValue>(currentInputs);
                latencyQueue.Enqueue((elapsed + currentLatency, valuesToApply));

                // Aplicar solo los valores cuya latencia ya ha expirado
                var effectiveInputs = new Dictionary<Guid, RuntimeValue>(currentInputs); // fallback to last known
                while (latencyQueue.Count > 0 && latencyQueue.Peek().releaseTime <= elapsed)
                {
                    var released = latencyQueue.Dequeue();
                    foreach (var kvp in released.values)
                    {
                        effectiveInputs[kvp.Key] = kvp.Value;
                    }
                }

                // --- INYECCIÓN DE FALLOS (Chaos Engineering) ---
                if (chaos != null)
                {
                    foreach (var key in effectiveInputs.Keys.ToList())
                    {
                        // Simular pérdida de paquete
                        if (random.NextDouble() < chaos.PacketLossChance)
                        {
                            effectiveInputs[key] = new RuntimeValue { VariableId = key, Value = PlcValue.Null(PlcDataType.Bool) };
                        }
                        // Simular Jitter
                        else if (random.NextDouble() < chaos.JitterChance)
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

                    if (!EvaluateInvariant(inv.Condition, ctx, ir))
                    {
                        result.IsSuccess = false;
                        result.Errors.Add($"Invariante violada en scan {scanRes.ScanNumber} (Paso: {step.Description}): {inv.Description}");
                        return result;
                    }
                }

                elapsed += scanInterval;
            } while (elapsed < step.SettleTimeMs);

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

    private bool EvaluateInvariant(string condition, IExpressionContext ctx, AtlasIrDocument ir)
    {
        if (string.IsNullOrEmpty(condition)) return true;
        
        try 
        {
            var node = ParseInvariantCondition(condition, ir);
            var result = _exprEngine.Evaluate(node, ctx);
            return result.Ok && result.Value.AsBool();
        }
        catch
        {
            return false; // Fail-closed on evaluation error
        }
    }

    private ExpressionNode ParseInvariantCondition(string condition, AtlasIrDocument ir)
    {
        condition = condition.Trim();
        if (condition.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return new ConstantExpression { DataType = "Bool", Value = "true" };
        if (condition.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return new ConstantExpression { DataType = "Bool", Value = "false" };

        // Simple "Variable == true" or "Variable"
        var parts = condition.Split(new[] { "==", " = " }, StringSplitOptions.RemoveEmptyEntries);
        var varKey = parts[0].Trim();
        var variable = ir.Variables.FirstOrDefault(v => v.Key == varKey);
        
        if (variable == null) throw new Exception($"Invariant variable {varKey} not found");

        if (parts.Length == 1 || (parts.Length == 2 && parts[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase)))
        {
            return new VariableExpression { VariableId = variable.Id, VariableKey = variable.Key };
        }
        
        if (parts.Length == 2 && parts[1].Trim().Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return new NotExpression { Operand = new VariableExpression { VariableId = variable.Id, VariableKey = variable.Key } };
        }

        throw new NotSupportedException($"Invariant condition '{condition}' is too complex for the simple parser. Use a formal ExpressionNode.");
    }

    public sealed class ScenarioResult
    {
        public string ScenarioName { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
