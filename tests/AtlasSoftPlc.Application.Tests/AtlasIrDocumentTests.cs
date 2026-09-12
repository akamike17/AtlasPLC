using AtlasSoftPlc.Application.Ir;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Domain.Ir;
using AtlasSoftPlc.Domain.Logic;
using AtlasSoftPlc.Domain.Values;
using AtlasSoftPlc.Domain.Variables;
using AtlasSoftPlc.Infrastructure.Persistence;

namespace AtlasSoftPlc.Application.Tests;

/// <summary>FASE C — IR mínima canónica (spec §39 FASE C).</summary>
public class AtlasIrDocumentTests
{
    [Fact]
    public void MotorFixture_IsValid()
    {
        var doc = AtlasIrFixtures.BuildMotorStopGuard();
        Assert.True(doc.IsValid());
        Assert.Equal(4, doc.Variables.Count); // 3 inputs + 1 output
        Assert.Equal(2, doc.Logic.Rules.Count); // paro dominante + marcha
    }

    [Fact]
    public void TankFixture_IsValid()
    {
        var doc = AtlasIrFixtures.BuildTank();
        Assert.True(doc.IsValid());
        Assert.Equal(4, doc.Variables.Count);
        Assert.Equal(3, doc.Logic.Rules.Count);
    }

    [Fact]
    public void InvalidName_MakesInvalid()
    {
        var doc = AtlasIrFixtures.BuildTank();
        // name es init-only; creamos otro doc con nombre vacío.
        var bad = new AtlasIrDocument { Name = "", Variables = doc.Variables };
        Assert.False(bad.IsValid());
    }

    [Fact]
    public void DuplicateVariableKeys_MakeInvalid()
    {
        var v1 = new VariableDefinition { Key = "A", DisplayName = "A" };
        var v2 = new VariableDefinition { Key = "A", DisplayName = "A2" };
        var doc = new AtlasIrDocument
        {
            Name = "x",
            Variables = new List<VariableDefinition> { v1, v2 },
        };
        Assert.False(doc.IsValid());
    }

    [Fact]
    public void MotorFixture_SafeState_HasMotorOff()
    {
        var doc = AtlasIrFixtures.BuildMotorStopGuard();
        var motor = doc.Variables.First(v => v.Key == "Motor");
        var safe = doc.SafeStates.Single(s => s.VariableId == motor.Id);
        Assert.False(safe.Value.AsBool());
    }

    [Fact]
    public void Tank_MigratesOneToOne_WithCatalogDemo()
    {
        // Equivalencia de regresión: la IR canónica y el demo actual coinciden en esencia.
        var ir = AtlasIrFixtures.BuildTank();
        var legacy = PlcProgramCatalog.BuildTankDemo();

        Assert.Equal(legacy.Name, ir.Name);
        Assert.Equal(legacy.Variables.Count, ir.Variables.Count);
        Assert.Equal(legacy.Logic.Rules.Count, ir.Logic.Rules.Count);
        Assert.Equal(legacy.Failsafe.Count, ir.SafeStates.Count);
    }

    [Fact]
    public void ToProgramDefinition_PreservesSafeStatesAsFailsafe()
    {
        var ir = AtlasIrFixtures.BuildTank();
        var def = ir.ToProgramDefinition();
        var pump = ir.Variables.First(v => v.Key == "Pump");
        Assert.True(def.Failsafe.ContainsKey(pump.Id));
        Assert.False(def.Failsafe[pump.Id].AsBool());
    }

    [Fact]
    public void AtlasIrDocument_RoundTrips_ThroughJson()
    {
        var doc = AtlasIrFixtures.BuildMotorStopGuard();
        var json = AtlasJson.Serialize(doc);
        var back = AtlasJson.Deserialize<AtlasIrDocument>(json);

        Assert.NotNull(back);
        Assert.Equal(doc.Name, back!.Name);
        Assert.Equal(doc.Variables.Count, back.Variables.Count);
        Assert.Equal(doc.Logic.Rules.Count, back.Logic.Rules.Count);
        Assert.Equal(doc.SafeStates.Count, back.SafeStates.Count);

        // El SafeState debe conservar el valor bool false tras serializar.
        var motor = doc.Variables.First(v => v.Key == "Motor");
        var safe = back.SafeStates.Single(s => s.VariableId == motor.Id);
        Assert.False(safe.Value.AsBool());
    }
}