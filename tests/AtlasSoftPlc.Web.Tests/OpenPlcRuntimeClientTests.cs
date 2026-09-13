using System.Net;
using AtlasSoftPlc.Targets;
using AtlasSoftPlc.Web.Services;

namespace AtlasSoftPlc.Web.Tests;

public sealed class OpenPlcRuntimeClientTests
{
    [Fact]
    public async Task ProbeUsesVersionCapabilitiesAndStatusHttpEndpoints()
    {
        var paths = new List<string>();
        var client = new OpenPlcRuntimeClient(() => new RecordingHandler(paths));
        var instance = new TargetInstance
        {
            Id = "openplc-local",
            TargetPluginId = "openplc",
            DisplayName = "OpenPLC local",
            Configuration = new Dictionary<string, string> { ["endpoint"] = "127.0.0.1", ["port"] = "8080" }
        };

        var result = await client.ProbeAsync(instance);

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "/api/version", "/api/capabilities", "/api/status" }, paths);
    }

    [Fact]
    public async Task MissingEndpointIsNotReportedAsConnected()
    {
        var client = new OpenPlcRuntimeClient(() => new RecordingHandler(new List<string>()));
        var result = await client.ProbeAsync(new TargetInstance { Id = "openplc", TargetPluginId = "openplc", DisplayName = "OpenPLC" });
        Assert.False(result.Succeeded);
        Assert.Equal("NotConfigured", result.State);
    }

    private sealed class RecordingHandler(ICollection<string> paths) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}")
            });
        }
    }
}
