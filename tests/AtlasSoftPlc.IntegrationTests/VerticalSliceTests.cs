using AtlasSoftPlc.Application.Ir;
using AtlasSoftPlc.Domain.Common;
using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Runtime.Engine;

namespace AtlasSoftPlc.IntegrationTests;

/// <summary>
/// FASE E — Prueba vertical (spec §39 FASE E): crear el proyecto mínimo vía la IR
/// canónica, y probar los invariantes contra el runtime real (simulación determinista).
///
/// Proyecto: Motor = Start AND NOT Stop AND GuardClosed
/// Invariantes:
///   Stop        → NOT Motor
///   NOT GuardClosed → NOT Motor
/// </summary>
public class VerticalSliceTests
{
    private static Dictionary<Guid, RuntimeValue> Inputs(
        AtlasIrDocument doc,
        bool start, bool stop, bool guardClosed)
    {
        Guid Id(string key) => doc.Variables.First(v => v.Key == key).Id;
        return new Dictionary<Guid, RuntimeValue>
        {
            [Id("Start")] = new RuntimeValue { VariableId = Id("Start"), Value = PlcValue.Bool(start), Quality = Quality.Good },
            [Id("Stop")] = new RuntimeValue { VariableId = Id("Stop"), Value = PlcValue.Bool(stop), Quality = Quality.Good },
            [Id("GuardClosed")] = new RuntimeValue { VariableId = Id("GuardClosed"), Value = PlcValue.Bool(guardClosed), Quality = Quality.Good },
        };
    }

    private static ScanCoordinator.ScanResult Scan(AtlasIrDocument doc, Dictionary<Guid, RuntimeValue> inputs)
    {
        var defs = doc.VariablesById;
        var motor = doc.Variables.First(v => v.Key == "Motor");
        var failsafe = doc.SafeStates.ToDictionary(s => s.VariableId, s => s.Value);
        var outputTypes = doc.Variables
            .Where(v => v.Direction == VariableDirection.Output)
            .ToDictionary(v => v.Id, v => v.DataType);

        return new ScanCoordinator().Scan(
            doc.Logic, defs, inputs, new Dictionary<Guid, RuntimeValue>(),
            doc.Interlocks, failsafe, outputTypes, 50);
    }

    [Fact]
    public void Motor_On_OnlyWhen_StartAndNotStopAndGuardClosed()
    {
        var doc = AtlasIrFixtures.BuildMotorStopGuard();
        var motorId = doc.Variables.First(v => v.Key == "Motor").Id;

        // Caso feliz: Start=true, Stop=false, GuardClosed=true → Motor ON.
        var on = Scan(doc, Inputs(doc, start: true, stop: false, guardClosed: true));
        Assert.True(on.Outputs.Get(motorId)!.Value.AsBool());
    }

    [Fact]
    public void Invariant_StopImpliesNotMotor()
    {
        var doc = AtlasIrFixtures.BuildMotorStopGuard();
        var motorId = doc.Variables.First(v => v.Key == "Motor").Id;

        // Stop=true debe dominar: Motor OFF aunque Start y Guard estén activos.
        var r = Scan(doc, Inputs(doc, start: true, stop: true, guardClosed: true));
        Assert.False(r.Outputs.Get(motorId)!.Value.AsBool());
    }

    [Fact]
    public void Invariant_NotGuardClosedImpliesNotMotor()
    {
        var doc = AtlasIrFixtures.BuildMotorStopGuard();
        var motorId = doc.Variables.First(v => v.Key == "Motor").Id;

        // Guard abierta: Motor OFF aunque Start=true y Stop=false.
        var r = Scan(doc, Inputs(doc, start: true, stop: false, guardClosed: false));
        Assert.False(r.Outputs.Get(motorId)!.Value.AsBool());
    }

    [Fact]
    public void VerticalSlice_ProducesNoScanErrors()
    {
        var doc = AtlasIrFixtures.BuildMotorStopGuard();
        var r = Scan(doc, Inputs(doc, start: true, stop: false, guardClosed: true));
        Assert.False(r.HasErrors);
    }
}