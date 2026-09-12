using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Protocols.Modbus.Targets;
using AtlasSoftPlc.Runtime.Targets;
using AtlasSoftPlc.Targets;

namespace AtlasSoftPlc.Targets.ContractTests;

/// <summary>
/// Contratos comunes que TODO target adapter debe cumplir (spec §26).
/// Estos tests NO dependen de hardware: ejercitan reglas de diseño con fakes.
/// </summary>
public class TargetCapabilitiesTests
{
    [Fact]
    public void None_HasNoCapabilities()
    {
        Assert.True(TargetCapabilities.None.IsEmpty);
        Assert.False(TargetCapabilities.None.Supports(TargetCapability.GenerateSource));
    }

    [Fact]
    public void Supports_ReflectsDeclaredCapabilities()
    {
        var caps = new TargetCapabilities(new[] { TargetCapability.ReadLiveData, TargetCapability.GenerateSource });
        Assert.True(caps.Supports(TargetCapability.ReadLiveData));
        Assert.True(caps.Supports(TargetCapability.GenerateSource));
        Assert.False(caps.Supports(TargetCapability.DeployProgram));
        Assert.False(caps.IsEmpty);
    }
}

public class TargetProfileTests
{
    [Fact]
    public void InferLevel_MonitorsAtL1()
    {
        var caps = new TargetCapabilities(new[] { TargetCapability.ReadLiveData });
        Assert.Equal(TargetSupportLevel.L1_Monitor, TargetProfile.InferLevel(caps));
    }

    [Fact]
    public void InferLevel_GenerateOnlyAtL3()
    {
        var caps = new TargetCapabilities(new[] { TargetCapability.GenerateSource, TargetCapability.ReadLiveData });
        Assert.Equal(TargetSupportLevel.L3_GenerateCompatibleArtifact, TargetProfile.InferLevel(caps));
    }

    [Fact]
    public void InferLevel_DeployAtL5()
    {
        var caps = new TargetCapabilities(new[] { TargetCapability.DeployProgram });
        Assert.Equal(TargetSupportLevel.L5_DirectDeployment, TargetProfile.InferLevel(caps));
    }

    [Fact]
    public void InferLevel_VerifyAtL6()
    {
        var caps = new TargetCapabilities(new[] { TargetCapability.VerifyDeployment });
        Assert.Equal(TargetSupportLevel.L6_OnlineVerify, TargetProfile.InferLevel(caps));
    }

    [Fact]
    public void TargetIdentity_NotFullySpecified_WhenMissingModel()
    {
        var id = new TargetIdentity { Manufacturer = "Siemens", Family = "S7" }; // sin Model
        Assert.False(id.IsFullySpecified);
    }
}

public class AtlasRuntimeTargetAdapterTests
{
    private static PlcProgramDefinition EmptyProject() => new()
    {
        Name = "Test",
        Logic = new Domain.Logic.LogicProgram { Name = "Test" },
    };

    [Fact]
    public void DeclaresSimulationAndOnlineData_CapabilitiesHonestly()
    {
        // No se requiere runtime en vivo para verificar declaración de capacidades.
        var adapter = new AtlasRuntimeTargetAdapter(new Runtime.Hosting.PlcRuntimeService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Runtime.Hosting.PlcRuntimeService>.Instance,
            new Runtime.Hosting.RuntimeStateStore(),
            new Runtime.Hosting.WatchdogService(),
            new NullNotifier()));

        Assert.True(adapter.Capabilities.Supports(TargetCapability.Simulate));
        Assert.True(adapter.Capabilities.Supports(TargetCapability.ReadLiveData));
        Assert.True(adapter.Capabilities.Supports(TargetCapability.WriteLiveData));

        // Honestidad: el runtime de simulación NO genera ni despliega a PLC físico.
        Assert.False(adapter.Capabilities.Supports(TargetCapability.GenerateSource));
        Assert.False(adapter.Capabilities.Supports(TargetCapability.DeployProgram));
    }

    [Fact]
    public async Task Deploy_WithoutConfirmationToken_Fails()
    {
        var adapter = new AtlasRuntimeTargetAdapter(new Runtime.Hosting.PlcRuntimeService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Runtime.Hosting.PlcRuntimeService>.Instance,
            new Runtime.Hosting.RuntimeStateStore(),
            new Runtime.Hosting.WatchdogService(),
            new NullNotifier()));

        // Aun sin soportar Deploy, la regla de confirmación explícita es universal (base).
        var result = await adapter.DeployAsync(EmptyProject(), "");
        Assert.False(result.Success);
    }

    private sealed class NullNotifier : AtlasSoftPlc.Domain.Runtime.IRuntimeNotifier
    {
        public Task NotifySnapshotAsync(AtlasSoftPlc.Domain.Runtime.RuntimeSnapshotDto snapshot) => Task.CompletedTask;
        public Task NotifyOutputChangedAsync(Guid variableId, AtlasSoftPlc.Domain.Values.PlcValue value) => Task.CompletedTask;
        public Task NotifyInputChangedAsync(Guid variableId, AtlasSoftPlc.Domain.Values.PlcValue value) => Task.CompletedTask;
        public Task NotifyStateChangedAsync(AtlasSoftPlc.Domain.Runtime.RuntimeState state) => Task.CompletedTask;
    }
}

public class ModbusOnlineAdapterTests
{
    private static PlcProgramDefinition EmptyProject() => new() { Name = "Test" };

    [Fact]
    public void DeclaresOnlineDataOnly_NeverEngineeringCapabilities()
    {
        var adapter = new ModbusOnlineAdapter();

        Assert.True(adapter.Capabilities.Supports(TargetCapability.ReadLiveData));
        Assert.True(adapter.Capabilities.Supports(TargetCapability.WriteLiveData));
        Assert.True(adapter.Capabilities.Supports(TargetCapability.Discover));

        // Contrato crítico: Modbus NO es un target de despliegue de proyecto.
        Assert.False(adapter.Capabilities.Supports(TargetCapability.GenerateSource));
        Assert.False(adapter.Capabilities.Supports(TargetCapability.GenerateProject));
        Assert.False(adapter.Capabilities.Supports(TargetCapability.Compile));
        Assert.False(adapter.Capabilities.Supports(TargetCapability.DeployProgram));
        Assert.False(adapter.Capabilities.Supports(TargetCapability.VerifyDeployment));
    }

    [Fact]
    public async Task ValidateAsync_BlocksProjectEngineering()
    {
        var adapter = new ModbusOnlineAdapter();
        var report = await adapter.ValidateAsync(EmptyProject());
        Assert.True(report.IsBlocked);
        Assert.NotEmpty(report.Messages);
    }

    [Fact]
    public async Task GenerateAsync_IsAlwaysUnsupported()
    {
        var adapter = new ModbusOnlineAdapter();
        var result = await adapter.GenerateAsync(EmptyProject());
        Assert.False(result.Success);
        Assert.Contains("no soportada", result.Error!, StringComparison.OrdinalIgnoreCase);
    }
}

public class UnsupportedCapabilityExceptionTests
{
    [Fact]
    public void CarriesCapabilityAndMessage()
    {
        var ex = new UnsupportedCapabilityException(TargetCapability.DeployProgram);
        Assert.Equal(TargetCapability.DeployProgram, ex.Capability);
        Assert.Contains("DeployProgram", ex.Message);
    }
}