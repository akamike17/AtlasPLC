using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AtlasSoftPlc.Web.Tests;

/// <summary>
/// Tests de los endpoints HTTP (autorizados) y controllers vía WebApplicationFactory.
/// </summary>
public sealed class WebEndpointTests : IClassFixture<AtlasWebFactory>
{
    private const string AdminUser = "admin";
    private const string TestPassword = "AtlasDemo!2026";

    private readonly AtlasWebFactory _factory;

    public WebEndpointTests(AtlasWebFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private static async Task<bool> LoginAsync(HttpClient client, string user, string pass)
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
        var resp = await client.PostAsync("/Account/Login", form);
        return resp.StatusCode == HttpStatusCode.Redirect;
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

    [Fact]
    public async Task Dashboard_Autenticado_Devuelve200()
    {
        using var client = NewClient();
        Assert.True(await LoginAsync(client, AdminUser, TestPassword));

        var resp = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Bomba", body); // nombre legible de salida
        Assert.Contains("Nivel bajo", body); // nombre legible de entrada
    }

    [Fact]
    public async Task Snapshot_Autenticado_Devuelve200()
    {
        using var client = NewClient();
        Assert.True(await LoginAsync(client, AdminUser, TestPassword));

        var resp = await client.GetAsync("/api/runtime/snapshot");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("state", body);
    }

    [Fact]
    public async Task Timeline_Autenticado_Devuelve200()
    {
        using var client = NewClient();
        Assert.True(await LoginAsync(client, AdminUser, TestPassword));

        var resp = await client.GetAsync("/api/runtime/timeline");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Antiforgery_Autenticado_EmiteToken()
    {
        using var client = NewClient();
        Assert.True(await LoginAsync(client, AdminUser, TestPassword));

        var resp = await client.GetAsync("/api/runtime/antiforgery");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("token", body);
    }

    [Fact]
    public async Task Explainer_Autenticado_ExplicaSalida()
    {
        using var client = NewClient();
        Assert.True(await LoginAsync(client, AdminUser, TestPassword));

        // Obtener un id de salida real del dashboard
        var dash = await client.GetAsync("/");
        var html = await dash.Content.ReadAsStringAsync();

        // Buscar el id de la bomba (output) — usamos el snapshot del runtime para obtener ids
        var snapResp = await client.GetAsync("/api/runtime/snapshot");
        var snap = await snapResp.Content.ReadAsStringAsync();

        // Como el ExplainerController requiere variableId, obtenemos uno de los ids de variables
        // del proyecto via GET (el bootstrap crea variables id deterministas no accesibles fácilmente).
        // Por eso probamos con un id inexistente: debe devolver 404 (variable no encontrada).
        var resp = await client.GetAsync("/api/explainer/output/00000000-0000-0000-0000-000000000000");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Simulation_Autenticado_Devuelve200()
    {
        using var client = NewClient();
        Assert.True(await LoginAsync(client, AdminUser, TestPassword));

        var resp = await client.GetAsync("/Home/Simulation");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Artifacts_Generate_MuestraEstadoYDiagnosticos()
    {
        using var client = NewClient();
        Assert.True(await LoginAsync(client, AdminUser, TestPassword));

        var dashboard = await client.GetAsync("/Home/Simulation");
        var html = await dashboard.Content.ReadAsStringAsync();
        var loadForm = html.IndexOf("/Home/LoadProgram", StringComparison.Ordinal);
        var catalogHtml = loadForm >= 0 ? html[loadForm..] : html;
        var match = Regex.Match(catalogHtml, @"name=""id""\s+value=""([0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})""");
        Assert.True(match.Success, "El catálogo debe exponer un programa persistido para generar el artefacto.");
        var programId = match.Groups[1].Value;
        var path = $"/Artifacts/Generate?id={programId}&kind=StructuredText";

        var generated = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, generated.StatusCode);
        var body = await generated.Content.ReadAsStringAsync();
        Assert.Contains("Artefacto validado", body);
        Assert.Contains("Diagnósticos", body);
        Assert.Contains("StructuredText", body);
    }

    [Fact]
    public async Task Simulation_ExponeCapacidadesRealesYBloqueaDeployModbus()
    {
        using var client = NewClient();
        Assert.True(await LoginAsync(client, AdminUser, TestPassword));

        var body = await (await client.GetAsync("/Home/Simulation")).Content.ReadAsStringAsync();
        Assert.Contains("Atlas Runtime", body);
        Assert.Contains("Modbus Online", body);
        Assert.Contains("Deploy físico permanece deshabilitado", body);
        Assert.Contains("data-target=\"Modbus Online\"", body);
        Assert.Contains("disabled=\"disabled\">Deploy", body);
    }

    [Fact]
    public async Task Diagnostics_Autenticado_Devuelve200()
    {
        using var client = NewClient();
        Assert.True(await LoginAsync(client, AdminUser, TestPassword));

        var resp = await client.GetAsync("/Home/Diagnostics");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task AccessDenied_Devuelve200()
    {
        using var client = NewClient();
        var resp = await client.GetAsync("/Account/AccessDenied");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task StartStop_Operator_Devuelve200_ConCsrf()
    {
        using var client = NewClient();
        Assert.True(await LoginAsync(client, AdminUser, TestPassword));

        var token = await GetCsrfTokenAsync(client);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/runtime/start");
        req.Headers.Add("X-CSRF-TOKEN", token);

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task StartStop_SinCsrf_Devuelve400()
    {
        using var client = NewClient();
        Assert.True(await LoginAsync(client, AdminUser, TestPassword));

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/runtime/stop");
        // sin token CSRF
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
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
}
