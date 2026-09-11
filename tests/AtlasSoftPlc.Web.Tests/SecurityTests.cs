using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AtlasSoftPlc.Web.Tests;

/// <summary>
/// Pruebas de seguridad web (auditoría hostil): autorización, CSRF e IDOR.
/// Usa cliente por test con aislamiento de cookies.
/// </summary>
public sealed class SecurityTests : IClassFixture<AtlasWebFactory>
{
    // Credenciales de semilla de Development (appsettings.Development.json).
    private const string AdminUser = "admin";
    private const string TestPassword = "AtlasDemo!2026";

    private readonly AtlasWebFactory _factory;

    public SecurityTests(AtlasWebFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    [Fact]
    public async Task Dashboard_SinAutenticacion_RedirigeALogin()
    {
        using var client = NewClient();
        var resp = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Account/Login", resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Snapshot_SinAutenticacion_RedirigeALogin()
    {
        using var client = NewClient();
        var resp = await client.GetAsync("/api/runtime/snapshot");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
    }

    [Fact]
    public async Task Mutaciones_SinAutenticacion_Redirigen()
    {
        using var client = NewClient();
        var stop = await client.PostAsync("/api/runtime/stop", null);
        var start = await client.PostAsync("/api/runtime/start", null);

        Assert.Equal(HttpStatusCode.Redirect, stop.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
    }

    [Fact]
    public async Task SetInput_GuidInexistente_Devuelve404()
    {
        using var client = NewClient();
        Assert.True(await LoginAsync(client, AdminUser, TestPassword));

        // Obtener token CSRF
        var token = await GetCsrfTokenAsync(client);
        
        var payload = JsonContent.Create(new { value = true });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/runtime/inputs/00000000-0000-0000-0000-000000000000")
        {
            Content = payload
        };
        req.Headers.Add("X-CSRF-TOKEN", token);

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Login_UsuarioInvalido_Rechaza()
    {
        using var client = NewClient();
        var resp = await PostLoginAsync(client, "admin", "contraseña-incorrecta");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("incorrectos", body);
    }

    [Fact]
    public async Task Logout_Desautentica()
    {
        using var client = NewClient();
        Assert.True(await LoginAsync(client, AdminUser, TestPassword));

        // Logout via form (requiere antiforgery)
        var token = ExtractAntiforgeryToken(await (await client.GetAsync("/")).Content.ReadAsStringAsync());
        var logoutForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });
        var logout = await client.PostAsync("/Account/Logout", logoutForm);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);

        var after = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
    }

    private static async Task<bool> LoginAsync(HttpClient client, string user, string pass)
    {
        var resp = await PostLoginAsync(client, user, pass);
        return resp.StatusCode == HttpStatusCode.Redirect;
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string user, string pass)
    {
        var page = await client.GetAsync("/Account/Login");
        var html = await page.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Username"] = user,
            ["Password"] = pass
        });
        return await client.PostAsync("/Account/Login", form);
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        var resp = await client.GetAsync("/api/runtime/antiforgery");
        var json = await resp.Content.ReadAsStringAsync();
        var idx = json.IndexOf("\"token\":\"", StringComparison.Ordinal);
        var start = idx + "\"token\":\"".Length;
        var end = json.IndexOf('"', start);
        return json[start..end];
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var marker = "name=\"__RequestVerificationToken\"";
        var idx = html.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return string.Empty;
        var valueIdx = html.IndexOf("value=\"", idx, StringComparison.Ordinal);
        var start = valueIdx + "value=\"".Length;
        var end = html.IndexOf('"', start);
        return html[start..end];
    }
}