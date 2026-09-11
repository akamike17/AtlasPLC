using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AtlasSoftPlc.Web.Tests;

/// <summary>
/// Pruebas de la autenticación reforzada (Argon2id + lockout + rate limiting).
/// </summary>
public sealed class AuthTests : IClassFixture<AtlasWebFactory>
{
    private const string AdminUser = "admin";
    private const string TestPassword = "AtlasDemo!2026";

    private readonly AtlasWebFactory _factory;

    public AuthTests(AtlasWebFactory factory) => _factory = factory;

    private HttpClient NewClient(HttpClientHandler handler) => new(handler, disposeHandler: false);
    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Login_CredencialesCorrectas_Redirige()
    {
        using var client = NewClient();
        var form = await BuildLoginForm(client, AdminUser, TestPassword);
        var resp = await client.PostAsync("/Account/Login", form);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
    }

    [Fact]
    public async Task Login_ContraseñaIncorrecta_Rechaza()
    {
        using var client = NewClient();
        var form = await BuildLoginForm(client, AdminUser, "contraseña-equivocada");
        var resp = await client.PostAsync("/Account/Login", form);
        // Rechazo devuelve 200 (re-render de la vista con error), no redirect.
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Login_UsuarioInexistente_Rechaza()
    {
        using var client = NewClient();
        var form = await BuildLoginForm(client, "no-existe", TestPassword);
        var resp = await client.PostAsync("/Account/Login", form);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Login_SinAntiforgery_Rechaza400()
    {
        using var client = NewClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = AdminUser,
            ["Password"] = TestPassword,
        });
        var resp = await client.PostAsync("/Account/Login", form);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Lockout_CincoFallos_BloqueaAunqueContraseñaCorrecta()
    {
        using var client = NewClient();

        // 5 intentos fallidos (mismo usuario)
        for (var i = 0; i < 5; i++)
        {
            var bad = await BuildLoginForm(client, AdminUser, $"mala-{i}");
            var resp = await client.PostAsync("/Account/Login", bad);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        // El 6º intento, aunque sea con la contraseña correcta, está bloqueado.
        var good = await BuildLoginForm(client, AdminUser, TestPassword);
        var blocked = await client.PostAsync("/Account/Login", good);
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode); // no redirige
        var body = await blocked.Content.ReadAsStringAsync();
        Assert.Contains("bloqueada", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasswordHasher_Verify_Roundtrip()
    {
        var hasher = new AtlasSoftPlc.Web.Auth.PasswordHasher();
        var hash = hasher.Hash("contraseña-segura-123");
        Assert.StartsWith("argon2id$", hash);
        Assert.True(hasher.Verify("contraseña-segura-123", hash));
        Assert.False(hasher.Verify("contraseña-incorrecta", hash));
    }

    [Fact]
    public void PasswordHasher_HashUnicoPorSal()
    {
        var hasher = new AtlasSoftPlc.Web.Auth.PasswordHasher();
        var h1 = hasher.Hash("misma-contraseña");
        var h2 = hasher.Hash("misma-contraseña");
        // Sal aleatoria => hashes distintos (defensa contra rainbow tables).
        Assert.NotEqual(h1, h2);
    }

    private static async Task<FormUrlEncodedContent> BuildLoginForm(HttpClient client, string user, string pass)
    {
        var page = await client.GetAsync("/Account/Login");
        var html = await page.Content.ReadAsStringAsync();
        var marker = "name=\"__RequestVerificationToken\"";
        var idx = html.IndexOf(marker, StringComparison.Ordinal);
        var valueIdx = html.IndexOf("value=\"", idx, StringComparison.Ordinal);
        var start = valueIdx + "value=\"".Length;
        var end = html.IndexOf('"', start);
        var token = html[start..end];

        return new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Username"] = user,
            ["Password"] = pass,
        });
    }
}