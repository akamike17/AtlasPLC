using AtlasSoftPlc.Web.Controllers;
using Microsoft.Extensions.Configuration;

namespace AtlasSoftPlc.Web.Tests;

public sealed class LabProtocolRunnerTests
{
    [Fact]
    public async Task RunAllExecutesProbeOnce()
    {
        var profile = new LabProfile { Id = "dotnet", TargetPluginId = "dotnet", DisplayName = "dotnet", Executable = "dotnet", Arguments = new[] { "--version" } };
        var result = await LabProtocolRunner.RunAsync(profile, CancellationToken.None);
        Assert.StartsWith("PROTOCOL PROBE PASS:", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProtocolProbeDoesNotClaimProgramExecution()
    {
        var profile = new LabProfile { Id = "dotnet", TargetPluginId = "dotnet", DisplayName = "dotnet", Executable = "dotnet", Arguments = new[] { "--version" } };
        var result = await LabProtocolRunner.RunAsync(profile, CancellationToken.None);
        Assert.DoesNotContain("PROGRAM PASS", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PROTOCOL PROBE", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LabProfileControlsExecutableAndEndpoint()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LabProfiles:custom:TargetPluginId"] = "custom",
            ["LabProfiles:custom:DisplayName"] = "Custom",
            ["LabProfiles:custom:Executable"] = "dotnet",
            ["LabProfiles:custom:Arguments:0"] = "--version",
            ["LabProfiles:custom:WorkingDirectory"] = "C:\\work",
            ["LabProfiles:custom:TimeoutSeconds"] = "7"
        }).Build();
        var profile = Assert.Single(new ConfigurationLabProfileRegistry(configuration).GetAll());
        Assert.Equal("dotnet", profile.Executable);
        Assert.Equal("C:\\work", profile.WorkingDirectory);
        Assert.Equal(7, profile.TimeoutSeconds);
    }
}
