using System.Security.Claims;
using AtlasSoftPlc.Application.Logic;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Infrastructure.Persistence;
using AtlasSoftPlc.Runtime.Hosting;
using AtlasSoftPlc.Web.Hubs;
using AtlasSoftPlc.Web.Auth;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// ---- MVC + antiforgery + SignalR ----
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();

// Antiforgery para APIs JSON (sección de seguridad): token esperado en el header.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
});

// Política de autorización global: por defecto TODO requiere usuario autenticado.
// Los endpoints públicos se marcan explícitamente con [AllowAnonymous].
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ---- Persistencia SQLite (sección 45/60) ----
// La BD vive SIEMPRE fuera del árbol de código (datos de runtime jamás en el repo).
// Path por defecto: %LocalAppData%/AtlasSoftPlc/atlas.db. Override vía config:
//   ConnectionStrings:Default (path completo a un .db) o DataDir (directorio).
var dbPath = builder.Configuration["ConnectionStrings:Default"];
if (string.IsNullOrWhiteSpace(dbPath))
{
    var dataDir = builder.Configuration["DataDir"];
    if (string.IsNullOrWhiteSpace(dataDir))
        dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AtlasSoftPlc");
    dbPath = Path.Combine(dataDir, "atlas.db");
}

var sqliteStore = new SqliteStore(dbPath!);
sqliteStore.EnsureCreated();

builder.Services.AddSingleton(sqliteStore);
builder.Services.AddSingleton<IProjectRepository, SqliteProjectRepository>();
builder.Services.AddSingleton<IVariableRepository, SqliteVariableRepository>();
builder.Services.AddSingleton<ILogicProgramRepository, SqliteLogicProgramRepository>();
builder.Services.AddSingleton<IAuditRepository, SqliteAuditRepository>();
builder.Services.AddSingleton<IHistorianRepository, SqliteHistorianRepository>();
builder.Services.AddSingleton<IAlarmRepository, SqliteAlarmRepository>();
builder.Services.AddSingleton<IProgramVersionRepository, SqliteProgramVersionRepository>();

// ---- Application services ----
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<VariableService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<HistorianService>();
builder.Services.AddScoped<AlarmService>();
builder.Services.AddScoped<ProgramVersionService>();
builder.Services.AddScoped<ConfigurationHasher>();
builder.Services.AddScoped<ValidationService>(sp => new ValidationService(new IValidationRule[]
{
    new ReferencesValidationRule(),
    new FailsafeValidationRule()
}));

// ---- Autenticación (Argon2id + lockout + rate limiting) ----
var authConfig = builder.Configuration;

var authOptions = new AuthOptions
{
    MaxFailedAttempts = int.TryParse(authConfig["Auth:MaxFailedAttempts"], out var mfa) ? mfa : 5,
    MaxAttemptsPerWindow = int.TryParse(authConfig["Auth:MaxAttemptsPerWindow"], out var mapw) ? mapw : 20,
    LockoutDuration = TimeSpan.TryParse(authConfig["Auth:LockoutDuration"], out var ld) ? ld : TimeSpan.FromMinutes(15),
    RateLimitWindow = TimeSpan.TryParse(authConfig["Auth:RateLimitWindow"], out var rlw) ? rlw : TimeSpan.FromMinutes(1),
};
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton<AtlasSoftPlc.Web.Auth.IUserStore, AtlasSoftPlc.Web.Auth.SqliteUserStore>();
builder.Services.AddSingleton<AtlasSoftPlc.Web.Auth.PasswordHasher>();
builder.Services.AddSingleton<AtlasSoftPlc.Web.Auth.AuthService>();
builder.Services.AddSingleton<AtlasSoftPlc.Web.Auth.UserSeeder>();

// ---- Runtime ----
builder.Services.AddSingleton<RuntimeStateStore>();
builder.Services.AddSingleton<WatchdogService>();
builder.Services.AddSingleton<PlcRuntimeService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PlcRuntimeService>());
builder.Services.AddSingleton<AtlasSoftPlc.Web.Services.SimulationService>();

// SignalR notifier
builder.Services.AddSingleton<IRuntimeNotifier, SignalRRuntimeNotifier>();

// ---- Auth (cookies locales, sección 61) ----
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        // En producción la cookie SIEMPRE se envía por HTTPS (nunca sobre HTTP plano).
        // En desarrollo SameAsRequest permite el run local HTTP sin romper el flujo.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Sembrar usuarios iniciales (solo si la tabla está vacía).
app.Services.GetRequiredService<AtlasSoftPlc.Web.Auth.UserSeeder>().SeedIfEmpty();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Cabeceras de seguridad (defensa en profundidad).
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    // CSP: solo recursos del propio origen + scripts necesarios para SignalR/Bootstrap.
    // 'unsafe-inline' limitado a style/script del propio Razor; en una SPA estricta se eliminaría.
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "connect-src 'self' ws: wss:; " +
        "frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    await next();
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<RuntimeHub>("/hubs/runtime");

app.Run();

public partial class Program { }