using System.Security.Claims;
using AtlasSoftPlc.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasSoftPlc.Web.Controllers;

/// <summary>
/// Autenticación local por cookies con roles (sección 61).
/// Credenciales verificadas contra el store de usuarios (Argon2id), con lockout
/// y rate limiting anti fuerza bruta. Sin credenciales hardcodeadas en el binario:
/// los usuarios se siembran desde configuración (appsettings / variables de entorno).
/// </summary>
public sealed class AccountController : Controller
{
    private readonly AuthService _auth;
    private readonly ILogger<AccountController> _logger;

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.Ordinal)
    {
        ["Administrator"] = "Administrador",
        ["Operator"] = "Operador",
        ["Engineer"] = "Ingeniero",
        ["Viewer"] = "Observador",
    };

    public AccountController(AuthService auth, ILogger<AccountController> logger)
    {
        _auth = auth;
        _logger = logger;
    }

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

        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var username = model.Username?.Trim() ?? string.Empty;

        switch (_auth.Authenticate(username, model.Password ?? string.Empty, remoteIp))
        {
            case LoginResult.LockedOut:
                _logger.LogWarning("Login bloqueado por lockout para '{User}' desde {Ip}", username, remoteIp);
                ModelState.AddModelError(string.Empty, "Cuenta bloqueada temporalmente por demasiados intentos fallidos. Reintente más tarde.");
                return View(model);

            case LoginResult.RateLimited:
                _logger.LogWarning("Login limitado por tasa desde {Ip}", remoteIp);
                ModelState.AddModelError(string.Empty, "Demasiados intentos. Espere un momento y reintente.");
                return View(model);

            case LoginResult.InvalidCredentials:
                _logger.LogWarning("Credenciales inválidas para '{User}' desde {Ip}", username, remoteIp);
                ModelState.AddModelError(string.Empty, "Usuario o contraseña incorrectos.");
                return View(model);

            case LoginResult.Success:
                break;
            default:
                ModelState.AddModelError(string.Empty, "Error inesperado.");
                return View(model);
        }

        var user = _auth.FindUser(username)!;
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
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