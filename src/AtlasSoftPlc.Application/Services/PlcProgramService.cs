using System.Security.Cryptography;
using System.Text;
using AtlasSoftPlc.Domain.Projects;

namespace AtlasSoftPlc.Application.Services;

/// <summary>Contrato de repositorio de la biblioteca de programas PLC (implementado en Infrastructure).</summary>
public interface IPlcProgramRepository
{
    Task<IReadOnlyList<PlcProgramDefinition>> GetAllAsync(CancellationToken ct = default);
    Task<PlcProgramDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(PlcProgramDefinition program, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Servicio de la biblioteca de programas PLC (sección biblioteca). Lista, guarda y carga
/// programas. No ejecuta industrial ni muta el runtime; el encendido/parada es del runtime.
/// </summary>
public sealed class PlcProgramService
{
    private readonly IPlcProgramRepository _repo;

    public PlcProgramService(IPlcProgramRepository repo) => _repo = repo;

    public Task<IReadOnlyList<PlcProgramDefinition>> GetAllAsync(CancellationToken ct = default)
        => _repo.GetAllAsync(ct);

    public Task<PlcProgramDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _repo.GetByIdAsync(id, ct);

    public async Task<PlcProgramDefinition> SaveAsync(PlcProgramDefinition program, CancellationToken ct = default)
    {
        program.UpdatedUtc = DateTime.UtcNow;
        program.Hash = ComputeHash(program);
        await _repo.SaveAsync(program, ct);
        return program;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default) => _repo.DeleteAsync(id, ct);

    /// <summary>Si la biblioteca está vacía, siembra los programas iniciales.</summary>
    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        var existing = await _repo.GetAllAsync(ct);
        if (existing.Count > 0)
            return;

        await _repo.SaveAsync(PlcProgramCatalog.BuildTankDemo(), ct);
        await _repo.SaveAsync(PlcProgramCatalog.BuildIrrigationDemo(), ct);
    }

    /// <summary>Hash SHA-256 determinista del programa (nombre+versión+lógica+failsafe+mapa).</summary>
    public static string ComputeHash(PlcProgramDefinition program)
    {
        var sb = new StringBuilder();
        sb.Append(program.Name).Append('|').Append(program.Version);
        foreach (var v in program.Variables.OrderBy(x => x.Id))
            sb.Append('|').Append(v.Id).Append(':').Append(v.Key).Append(':').Append(v.Direction).Append(':').Append(v.DataType);
        sb.Append('|').Append(program.Logic.Rules.Count);
        foreach (var r in program.Logic.Rules.OrderBy(x => x.Id))
            sb.Append('|').Append(r.Id).Append(':').Append(r.Name).Append(':').Append(r.Priority);
        foreach (var fs in program.Failsafe.OrderBy(x => x.Key))
            sb.Append('|').Append(fs.Key).Append(':').Append(fs.Value.AsString());
        foreach (var m in program.ModbusMap.OrderBy(x => x.Key))
            sb.Append('|').Append(m.Key).Append(':').Append(m.Value);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }
}