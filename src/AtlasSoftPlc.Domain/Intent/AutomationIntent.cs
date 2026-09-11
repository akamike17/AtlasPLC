namespace AtlasSoftPlc.Domain.Intent;

/// <summary>Intención de automatización en lenguaje natural normalizada (sección 20).</summary>
public sealed class AutomationIntent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Trigger { get; set; } = string.Empty;
    public List<string> Conditions { get; set; } = new();
    public List<string> Actions { get; set; } = new();
    public List<string> StopConditions { get; set; } = new();
    public string RawText { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}