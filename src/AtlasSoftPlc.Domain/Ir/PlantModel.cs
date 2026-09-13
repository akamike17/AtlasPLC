namespace AtlasSoftPlc.Domain.Ir;

/// <summary>Modelo físico mínimo para validar requisitos y exclusiones entre actuadores.</summary>
public sealed class PlantModel
{
    public int SchemaVersion { get; init; } = 1;
    public List<PlantComponent> Components { get; init; } = new();
}

public sealed class PlantComponent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid VariableId { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool RequiresSafeState { get; init; } = true;
    public bool RequiresPhysicalPermission { get; init; }
    public List<Guid> Requires { get; init; } = new();
    public List<Guid> MutuallyExclusiveWith { get; init; } = new();
    public SafeState? SafeState { get; init; }
}
