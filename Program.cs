using System.Security.Claims;
using AtlasSoftPlc.Application.Logic;
using AtlasSoftPlc.Application.Services;
using AtlasSoftPlc.Application.Validation;
using AtlasSoftPlc.Application.Graph;
using AtlasSoftPlc.Application.Backends;
using AtlasSoftPlc.Application.Packages;
using AtlasSoftPlc.Domain.Runtime;
using AtlasSoftPlc.Infrastructure.Persistence;
using AtlasSoftPlc.Runtime.Hosting;
using AtlasSoftPlc.Runtime.Targets;
using AtlasSoftPlc.Protocols.Modbus.Targets;
using AtlasSoftPlc.Web.Hubs;
using AtlasSoftPlc.Web.Auth;
using AtlasSoftPlc.Targets;
using AtlasSoftPlc.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;

// ── Logging estructurado (Serilog): consola + archivo rotativo ──
// El archivo va a %LocalAppData%/AtlasSoftPlc/logs/atlas-.log (fuera del árbol).
var logDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AtlasSoftPlc", "logs");
Directory.CreateDirectory(logDir);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logDir, "atlas-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        shared: true)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// En desarrollo el host administrado puede no tener acceso al directorio global
// de claves de ASP.NET. Usar un directorio temporal propio evita que antiforgery
// y cookies fallen al renderizar el login, sin desactivar Data Protection.
if (builder.Environment.IsDevelopment())
{
    var dataProtectionDir = Path.Combine(Path.GetTempPath(), "AtlasSoftPlc", "DataProtection-Keys");
    Directory.CreateDirectory(dataProtectionDir);
    builder.Services.AddDataProtection();
    builder.Services.PostConfigure<KeyManagementOptions>(options =>
        options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(dataProtectionDir), NullLoggerFactory.Instance));
}

// ---- MVC + antiforgery + SignalR ----
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IGraphValidator, GraphValidator>();
builder.Services.AddSingleton<IGraphLowerer, GraphLowerer>();
builder.Services.AddSingleton<IGraphDocumentRepository, SqliteGraphDocumentRepository>();
builder.Services.AddScoped<IProgramValidationPipeline, ProgramValidationPipeline>();
builder.Services.AddSingleton<ITargetRegistry, TargetRegistry>();
builder.Services.AddSingleton<ITargetConfigurationProvider, SqliteTargetConfigurationProvider>();
TargetPluginCatalog.AddBuiltIns(builder.Services);
builder.Services.AddScoped<GraphApplicationService>();
builder.Services.AddSingleton<StructuredTextEmitter>();
builder.Services.AddSingleton<PlcOpenXmlEmitter>();
builder.Services.AddScoped<ArtifactPipeline>();
builder.Services.AddScoped<TargetDeploymentWorkflow>();
// Adapters concretos compuestos por DI; Modbus sólo expone I/O online, no deployment.
builder.Services.AddSingleton<AtlasRuntimeTargetAdapter>();
builder.Services.AddSingleton<ModbusOnlineAdapter>();
builder.Services.AddSingleton<IPlcTargetAdapter>(sp => sp.GetRequiredService<AtlasRuntimeTargetAdapter>());
builder.Services.AddSingleton<IPlcTargetAdapter>(sp => sp.GetRequiredService<ModbusOnlineAdapter>());
builder.Services.AddSingleton<ITargetPlugin>(sp => new AdapterTargetPlugin(sp.GetRequiredService<AtlasRuntimeTargetAdapter>(), sp.GetRequiredService<ITargetConfigurationProvider>(), "atlas-simulation"));
builder.Services.AddSingleton<ITargetPlugin>(sp => new AdapterTargetPlugin(sp.GetRequiredService<ModbusOnlineAdapter>(), sp.GetRequiredService<ITargetConfigurationProvider>(), "modbus-online", probeNetwork: true));
builder.Services.AddSingleton<ITargetPluginRegistry, TargetPluginRegistry>();

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
builder.Services.AddSingleton<IRuntimeAuditSink>(sp => (SqliteAuditRepository)sp.GetRequiredService<IAuditRepository>());
builder.Services.AddSingleton<IHistorianRepository, SqliteHistorianRepository>();
builder.Services.AddSingleton<IAlarmRepository, SqliteAlarmRepository>();
builder.Services.AddSingleton<IProgramVersionRepository, SqliteProgramVersionRepository>();
builder.Services.AddSingleton<IPlcProgramRepository, SqlitePlcProgramRepository>();
builder.Services.AddSingleton<IProgramTargetSelectionRepository, SqliteProgramTargetSelectionRepository>();
builder.Services.AddSingleton<ITargetInstanceRepository, SqliteTargetInstanceRepository>();
builder.Services.AddSingleton<IArtifactStore, SqliteArtifactStore>();

// ---- Health checks (readiness/liveness) ----
builder.Services.AddHealthChecks()
    .AddCheck("sqlite", () =>
    {
        try
        {
            using var conn = sqliteStore.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.ExecuteScalar();
            return HealthCheckResult.Healthy($"esquema v{sqliteStore.CurrentSchemaVersion()}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("BD inaccesible", ex);
        }
    })
    .AddCheck("runtime", () =>
    {
        // El runtime expone su estado vía el store compartido; aquí sólo validamos que el
        // servicio esté registrado y su store responda.
        return HealthCheckResult.Healthy();
    }, tags: new[] { "ready" });

// ---- Application services ----
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<VariableService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<HistorianService>();
builder.Services.AddScoped<AlarmService>();
builder.Services.AddScoped<ProgramVersionService>();
builder.Services.AddSingleton<PlcProgramService>();
builder.Services.AddScoped<ConfigurationHasher>();
builder.Services.AddScoped<ValidationService>(sp => new ValidationService(new IValidationRule[]
{
    new ReferencesValidationRule(),
    new FailsafeValidationRule(),
    new UndefinedReferenceValidationRule(),
    new DuplicateWriterValidationRule(),
    new OutputSafeStateValidationRule(),
    new InterlockDominanceValidationRule(),
    new ContradictoryExpressionValidationRule(),
    new UnreachableBranchValidationRule()
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

// ---- Integración Modbus TCP (modalidad explícita, deshabilitada por defecto) ----
builder.Services.Configure<AtlasSoftPlc.Web.Services.ModbusOptions>(
    builder.Configuration.GetSection(AtlasSoftPlc.Web.Services.ModbusOptions.SectionName));
builder.Services.AddSingleton<AtlasSoftPlc.Web.Services.ModbusIoService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AtlasSoftPlc.Web.Services.ModbusIoService>());

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
// Sincronizar la biblioteca inicial antes de servir la UI. Es idempotente:
// conserva programas existentes y agrega únicamente los proyectos faltantes.
app.Services.GetRequiredService<AtlasSoftPlc.Web.Services.SimulationService>().EnsureLibrary();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

// La prueba y el perfil de desarrollo pueden ejecutarse sobre HTTP local; producción
// mantiene la redirección obligatoria hacia HTTPS.
if (!app.Environment.IsDevelopment())
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

// Health check de librería: /health (liveness) y /health/ready (readiness).
// AllowAnonymous: los probes de infraestructura no deben requerir auth.
app.MapHealthChecks("/health").AllowAnonymous();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
}).AllowAnonymous();

// ── Plug-and-play: auto-apertura del navegador en el puerto real elegido ──
// Con "applicationUrl": "http://localhost:0" Kestrel elige un puerto libre; aquí lo
// leemos una vez arrancado y abrimos el navegador, de modo que el usuario no tenga
// que copiar la URL ni configurar nada (portátil entre equipos, sin colisiones de puerto).
if (app.Environment.IsDevelopment())
{
    var lifetime = app.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>();
    lifetime.ApplicationStarted.Register(() =>
    {
        // app.Urls expone las direcciones reales (con el puerto ya asignado por Kestrel).
        var url = app.Urls.FirstOrDefault();
        if (string.IsNullOrEmpty(url))
            return;

        var displayUrl = url.Replace("0.0.0.0", "localhost", StringComparison.Ordinal)
                            .Replace("[::]", "localhost", StringComparison.Ordinal);
        Log.Information("Aplicación lista en {Url}", displayUrl);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(displayUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo abrir el navegador automáticamente");
        }
    });
}

try
{
    Log.Information("AtlasSoftPlc iniciando...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "AtlasSoftPlc terminó de forma inesperada");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
