using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasSoftPlc.Web.Controllers;

/// <summary>
/// Autenticación local por cookies con roles (sección 61).
/// Para el MVP se mantiene un usuario local demo por rol; en producción esto
/// debe respaldarse en un store de usuarios con hash de contraseña (Argon2/BCrypt).
/// </summary>
public sealed class AccountController : Controller
{
    // USUARIOS DEMO — CONTRA SEÑALADA EN LA AUDITORÍA DE SEGURIDAD.
    // Credenciales fijas SOLO para el MVP local. En producción esto DEBE
    // sustituirse por un store de usuarios (Argon2id/BCrypt) y config externa.
    // No se mueven a appsettings porque el MVP no tiene proveedor de usuarios real;
    // cambiarlas aquí sin un sistema de auth real sería cosmético.
    private static readonly (string User, string Password, string Role)[] DemoUsers =
    {
        ("admin", "admin", "Administrator"),
        ("operador", "operador", "Operator"),
    };

    private static readonly Dictionary<string, string> DisplayNames = new()
    {
        ["Administrator"] = "Administrador",
        ["Operator"] = "Operador",
        ["Engineer"] = "Ingeniero",
        ["Viewer"] = "Observador",
    };

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
            return View(model);

        var match = DemoUsers.FirstOrDefault(u =>
            string.Equals(u.User, model.Username, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(u.Password, model.Password, StringComparison.Ordinal));

        if (match == default)
        {
            ModelState.AddModelError(string.Empty, "Usuario o contraseña incorrectos.");
            return View(model);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, match.User),
            new(ClaimTypes.Role, match.Role),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();
}

public sealed class LoginViewModel
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}