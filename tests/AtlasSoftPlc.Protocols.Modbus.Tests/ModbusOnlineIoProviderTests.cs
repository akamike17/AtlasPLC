using AtlasSoftPlc.Domain.Projects;
using AtlasSoftPlc.Targets;
using AtlasSoftPlc.Protocols.Modbus.Targets;

namespace AtlasSoftPlc.Protocols.Modbus.Tests;

public sealed class ModbusOnlineIoProviderTests
{
    [Fact]
    public async Task OnlineProviderConnectsReadsWritesAndReportsDiagnostics()
    {
        using var server = new ModbusTcpServer();
        server.Start();
        server.DataStore.CoilDiscretes.WritePoints(2, new[] { true });
        server.DataStore.HoldingRegisters.WritePoints(12, new ushort[] { 123 });
        var provider = new ModbusOnlineIoProvider();
        var instance = Instance(server.Port);

        var connected = await provider.ConnectAsync(instance);
        var read = await provider.ReadAsync(instance, new[] { "coil:2", "holdingregister:12" });
        var written = await provider.WriteAsync(instance, new Dictionary<string, string>
        {
            ["coil:2"] = "false",
            ["holdingregister:12"] = "456"
        });
        var diagnostics = await provider.DiagnosticsAsync(instance);

        Assert.True(connected.Succeeded, connected.Message);
        Assert.True(read.Succeeded, read.Message);
        Assert.Equal("True", read.Values!["coil:2"]);
        Assert.Equal("123", read.Values["holdingregister:12"]);
        Assert.True(written.Succeeded, written.Message);
        Assert.False(server.DataStore.CoilDiscretes.ReadPoints(2, 1)[0]);
        Assert.Equal((ushort)456, server.DataStore.HoldingRegisters.ReadPoints(12, 1)[0]);
        Assert.True(diagnostics.Succeeded, diagnostics.Message);
        Assert.Equal("Connected", diagnostics.Values!["state"]);
        Assert.True(int.Parse(diagnostics.Values["totalReads"]) >= 2);
        Assert.True(int.Parse(diagnostics.Values["totalWrites"]) >= 2);
    }

    [Fact]
    public async Task InvalidAddressIsRejectedBeforeNetworkIo()
    {
        var provider = new ModbusOnlineIoProvider();
        var result = await provider.ReadAsync(Instance(1), new[] { "not-an-address" });

        Assert.False(result.Succeeded);
        Assert.Equal("InvalidAddress", result.State);
    }

    [Fact]
    public async Task ClosedEndpointIsReportedAsFaulted()
    {
        var provider = new ModbusOnlineIoProvider();

        var result = await provider.ConnectAsync(Instance(1) with
        {
            Configuration = new Dictionary<string, string>
            {
                ["endpoint"] = "127.0.0.1",
                ["port"] = "1",
                ["timeoutMs"] = "100"
            }
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Faulted", result.State);
    }

    [Fact]
    public async Task ModbusAdapterCannotGenerateOrDeployAProgram()
    {
        var adapter = new ModbusOnlineAdapter();
        var program = new PlcProgramDefinition { Name = "online-io-only" };

        var compatibility = await adapter.ValidateAsync(program);
        var generated = await adapter.GenerateAsync(program);

        Assert.Equal(CompatibilityReport.CompatibilityStatus.Blocked, compatibility.Status);
        Assert.False(generated.Success);
        Assert.Contains("no soportada", generated.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.False(adapter.Profile.Capabilities.Supports(TargetCapability.GenerateSource));
        Assert.False(adapter.Profile.Capabilities.Supports(TargetCapability.DeployProgram));
    }

    private static TargetInstance Instance(int port) => new()
    {
        Id = "modbus-local",
        TargetPluginId = "modbus-online",
        TargetType = "modbus-online",
        DisplayName = "Modbus local",
        Configuration = new Dictionary<string, string>
        {
            ["endpoint"] = "127.0.0.1",
            ["port"] = port.ToString(),
            ["unitId"] = "1",
            ["timeoutMs"] = "500",
            ["retries"] = "1"
        }
    };
}
