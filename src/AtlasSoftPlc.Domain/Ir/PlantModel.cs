namespace AtlasSoftPlc.Domain.Ir;

/// <summary>
/// Define el tipo de componente físico en la planta.
/// Permite diferenciar el comportamiento y los requisitos de seguridad.
/// </summary>
public enum PlantComponentType
{
    Generic,
    Motor,
    Valve,
    Sensor,
    Heater,
    Pump
}

/// <summary>Modelo físico para validar requisitos, exclusiones y seguridad entre actuadores.</summary>
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
    public PlantComponentType Type { get; init; } = PlantComponentType.Generic;
    public bool RequiresSafeState { get; init; } = true;
    public bool RequiresPhysicalPermission { get; init; }
    public List<Guid> Requires { get; init; } = new();
    /// <summary>Binding requisito → variable lógica que debe estar activo.</summary>
    public Dictionary<Guid, Guid> RequirementBindings { get; init; } = new();
    public List<Guid> MutuallyExclusiveWith { get; init; } = new();
    public SafeState? SafeState { get; init; }
}
