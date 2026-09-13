using System.Net;
using System.Net.Http.Headers;
using AtlasSoftPlc.Targets;
using AtlasSoftPlc.Web.Services;

namespace AtlasSoftPlc.Web.Tests;

public sealed class OpenPlcRuntimeClientTests
{
    [Fact]
    public async Task ProbeUsesHttpsDefaultsAndAllHealthEndpoints()
    {
        var requests = new List<HttpRequestMessage>();
        var client = new OpenPlcRuntimeClient(() => new RecordingHandler(requests));

        var result = await client.ProbeAsync(Instance());

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "/api/version", "/api/capabilities", "/api/status" }, requests.Select(x => x.RequestUri!.AbsolutePath));
        Assert.All(requests, request => Assert.Equal("https", request.RequestUri!.Scheme));
        Assert.All(requests, request => Assert.Equal(8443, request.RequestUri!.Port));
    }

    [Fact]
    public async Task ExplicitBaseUrlIsUsedWithoutChangingIt()
    {
        var requests = new List<HttpRequestMessage>();
        var client = new OpenPlcRuntimeClient(() => new RecordingHandler(requests));

        await client.StatusAsync(Instance(new Dictionary<string, string> { ["baseUrl"] = "https://plc.local:9443/runtime" }));

        Assert.Equal("https://plc.local:9443/runtime/api/status", requests.Single().RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task InvalidHttpSchemeWithoutExplicitBaseUrlIsNotConfigured()
    {
        var client = new OpenPlcRuntimeClient(() => new RecordingHandler(new List<HttpRequestMessage>()));

        var result = await client.ProbeAsync(Instance(new Dictionary<string, string> { ["scheme"] = "http" }));

        Assert.False(result.Succeeded);
        Assert.Equal("NotConfigured", result.State);
    }

    [Fact]
    public async Task LoginStoresAccessTokenAndStatusSendsBearer()
    {
        var requests = new List<HttpRequestMessage>();
        var sessions = new OpenPlcSessionStore();
        var credentials = new InMemoryTargetCredentialProvider();
        credentials.Set("openplc-local", new TargetCredentials("operator", "external-secret"));
        var client = new OpenPlcRuntimeClient(() => new RecordingHandler(requests, request =>
            request.RequestUri!.AbsolutePath == "/api/login"
                ? Json(HttpStatusCode.OK, "{\"access_token\":\"jwt-memory-only\"}")
                : Json(HttpStatusCode.OK, "{\"state\":\"RUN\"}")), sessions, credentials);
        var instance = Instance(new Dictionary<string, string>
        {
            ["username"] = "legacy-value-must-be-ignored",
            ["password"] = "legacy-value-must-be-ignored"
        }) with { CredentialReference = "openplc-local" };

        var login = await client.LoginAsync(instance);
        var status = await client.StatusAsync(instance);

        Assert.True(login.Succeeded);
        Assert.Equal("Authenticated", login.State);
        Assert.True(status.Succeeded);
        Assert.Null(requests[0].Headers.Authorization);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "jwt-memory-only"), requests[1].Headers.Authorization);
    }

    [Fact]
    public async Task LoginWithoutDemonstratedTokenFailsAndDoesNotCreateSession()
    {
        var requests = new List<HttpRequestMessage>();
        var sessions = new OpenPlcSessionStore();
        var credentials = new InMemoryTargetCredentialProvider();
        credentials.Set("openplc-local", new TargetCredentials("operator", "external-secret"));
        var client = new OpenPlcRuntimeClient(() => new RecordingHandler(requests, _ => Json(HttpStatusCode.OK, "{\"ok\":true}")), sessions, credentials);

        var result = await client.LoginAsync(Instance() with { CredentialReference = "openplc-local" });

        Assert.False(result.Succeeded);
        Assert.Equal("LOGIN_FAILED", result.State);
        Assert.DoesNotContain("external-secret", result.Message, StringComparison.Ordinal);
        Assert.False(sessions.TryGet("openplc-local", out _));
    }

    [Fact]
    public async Task ProtectedUnauthorizedRemovesSessionWithoutRetryLoop()
    {
        var requests = new List<HttpRequestMessage>();
        var sessions = new OpenPlcSessionStore();
        sessions.Set("openplc-local", "expired-token");
        var client = new OpenPlcRuntimeClient(() => new RecordingHandler(requests, _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)), sessions);

        var result = await client.StatusAsync(Instance());

        Assert.False(result.Succeeded);
        Assert.Equal("Unauthorized", result.State);
        Assert.Single(requests);
        Assert.False(sessions.TryGet("openplc-local", out _));
    }

    [Fact]
    public async Task SelfSignedAcceptanceIsScopedToTheInstance()
    {
        var handlers = new List<HttpClientHandler>();
        var client = new OpenPlcRuntimeClient(() =>
        {
            var handler = new HttpClientHandler();
            handlers.Add(handler);
            return handler;
        });

        await client.StatusAsync(Instance(new Dictionary<string, string> { ["allowSelfSigned"] = "false" }));
        await client.StatusAsync(Instance(new Dictionary<string, string> { ["allowSelfSigned"] = "true" }));

        Assert.Null(handlers[0].ServerCertificateCustomValidationCallback);
        Assert.NotNull(handlers[1].ServerCertificateCustomValidationCallback);
    }

    [Fact]
    public void PluginDeclaresTheCompleteOpenPlcWorkflow()
    {
        var client = new OpenPlcRuntimeClient(() => new RecordingHandler(new List<HttpRequestMessage>()));
        var plugin = new OpenPlcTargetPlugin(client);

        Assert.Equal(new[] { "connect", "login", "status", "logs", "start", "stop" }, plugin.Actions.Select(x => x.Id));
        Assert.Equal("Assisted", plugin.Descriptor.ImplementationState.ToString());
        Assert.DoesNotContain(TargetCapability.GenerateSource, plugin.Descriptor.Capabilities.All);
        Assert.DoesNotContain(TargetCapability.DeployProgram, plugin.Descriptor.Capabilities.All);
    }

    private static TargetInstance Instance(IReadOnlyDictionary<string, string>? configuration = null) => new()
    {
        Id = "openplc-local",
        TargetPluginId = "openplc",
        DisplayName = "OpenPLC local",
        Configuration = configuration ?? new Dictionary<string, string>()
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body)
    };

    private sealed class RecordingHandler(
        ICollection<HttpRequestMessage> requests,
        Func<HttpRequestMessage, HttpResponseMessage>? responder = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(CloneRequest(request));
            return Task.FromResult(responder?.Invoke(request) ?? Json(HttpStatusCode.OK, "{\"ok\":true}"));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            if (request.Headers.Authorization is not null)
                clone.Headers.Authorization = request.Headers.Authorization;
            return clone;
        }
    }
}
