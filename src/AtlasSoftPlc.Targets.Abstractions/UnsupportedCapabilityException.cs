namespace AtlasSoftPlc.Targets;

/// <summary>
/// Lanzada (o representada como resultado) cuando un target no soporta una
/// capacidad concreta. Nunca se devuelve un éxito simulado (spec §11/§12).
/// </summary>
public sealed class UnsupportedCapabilityException : Exception
{
    public TargetCapability Capability { get; }

    public UnsupportedCapabilityException(TargetCapability capability)
        : base($"El target no soporta la capacidad '{capability}'.")
    {
        Capability = capability;
    }
}