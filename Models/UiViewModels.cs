namespace AtlasSoftPlc.Web.Models;

public sealed record IoPointViewModel
{
    public required Guid Id { get; init; }
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public string DataType { get; init; } = "BOOL";
    public required string Direction { get; init; }
    public bool Value { get; init; }
    public bool InitialValue { get; init; }
    public string? Binding { get; init; }
    public string? Address { get; init; }
    public string Quality { get; init; } = "Simulated";
    public bool Writable { get; init; }
    public bool Forced { get; init; }
}
