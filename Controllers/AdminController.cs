using AtlasSoftPlc.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasSoftPlc.Web.Controllers;

[Authorize(Roles = "Administrator")]
public sealed class AdminController(IUserStore users, PasswordHasher hasher) : Controller
{
    private static readonly string[] Roles = ["Administrator", "Engineer", "Operator", "Viewer"];

    [HttpGet]
    public IActionResult Users() => View(new AdminUsersViewModel(users.All().OrderBy(u => u.Username).ToList(), Roles));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SaveUser(string username, string password, string role, string? displayName)
    {
        username = username?.Trim() ?? string.Empty;
        if (username.Length is < 3 or > 64 || string.IsNullOrWhiteSpace(password) || !Roles.Contains(role, StringComparer.Ordinal))
        {
            TempData["AdminMessage"] = "Usuario inválido: usa nombre de 3–64 caracteres, contraseña y un rol permitido.";
            return RedirectToAction(nameof(Users));
        }
        users.Upsert(new UserAccount(username, hasher.Hash(password), role, string.IsNullOrWhiteSpace(displayName) ? username : displayName.Trim()));
        TempData["AdminMessage"] = $"Usuario '{username}' guardado con rol {role}.";
        return RedirectToAction(nameof(Users));
    }
}

public sealed record AdminUsersViewModel(IReadOnlyList<UserAccount> Users, IReadOnlyList<string> Roles);
