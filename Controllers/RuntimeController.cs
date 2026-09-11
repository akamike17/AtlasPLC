using AtlasSoftPlc.Runtime.Hosting;
using AtlasSoftPlc.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AtlasSoftPlc.Web.Controllers;

/// <summary>
/// API de operación y simulación (sección 42/63).
/// Regla de seguridad: mutaciones solo POST + [Authorize] + antiforgery (CSRF).
/// Nunca GET para acciones.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class RuntimeController : ControllerBase
{
    private readonly RuntimeStateStore _store;
    private readonly SimulationService _sim;
    private readonly IAntiforgery _antiforgery;

    public RuntimeController(RuntimeStateStore store, SimulationService sim, IAntiforgery antiforgery)
    {
        _store = store;
        _sim = sim;
        _antiforgery = antiforgery;
    }

    [HttpGet("snapshot")]
    public IActionResult Snapshot() => Ok(_store.Snapshot);

    [HttpGet("timeline")]
    public IActionResult Timeline() => Ok(_sim.Timeline);

    // Emite el token antiforgery para que el front lo envíe en el header X-CSRF-TOKEN.
    [HttpGet("antiforgery")]
    public IActionResult AntiforgeryToken()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }

    [HttpPost("inputs/{id}")]
    public async Task<IActionResult> SetInput(Guid id, [FromBody] SetInputRequest req)
    {
        // CSRF: validar token en mutaciones JSON.
        try
        {
            await _antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return BadRequest(new { error = "Antiforgery token inválido." });
        }

        // IDOR / validación de recurso: solo variables que existen y son de entrada.
        if (!_sim.TrySetInput(id, req.Value))
            return NotFound(new { error = "Variable de entrada no encontrada." });

        return Ok(new { ok = true });
    }

    [HttpPost("start")]
    [Authorize(Roles = "Administrator,Operator")]
    public async Task<IActionResult> Start()
    {
        try
        {
            await _antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return BadRequest(new { error = "Antiforgery token inválido." });
        }
        _sim.Start();
        return Ok(new { ok = true });
    }

    [HttpPost("stop")]
    [Authorize(Roles = "Administrator,Operator")]
    public async Task<IActionResult> Stop()
    {
        try
        {
            await _antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return BadRequest(new { error = "Antiforgery token inválido." });
        }
        _sim.Stop();
        return Ok(new { ok = true });
    }
}

public sealed class SetInputRequest
{
    public bool Value { get; set; }
}