using System.Text.Json.Serialization;

namespace AtlasSoftPlc.Domain.Logic;

/// <summary>Acciones disponibles en el IR (sección 12).</summary>
[JsonDerivedType(typeof(SetOutputAction), typeDiscriminator: "setOutput")]
[JsonDerivedType(typeof(SetMemoryAction), typeDiscriminator: "setMemory")]
[JsonDerivedType(typeof(ResetMemoryAction), typeDiscriminator: "resetMemory")]
[JsonDerivedType(typeof(StartTimerAction), typeDiscriminator: "startTimer")]
[JsonDerivedType(typeof(ResetTimerAction), typeDiscriminator: "resetTimer")]
[JsonDerivedType(typeof(IncrementCounterAction), typeDiscriminator: "incrementCounter")]
[JsonDerivedType(typeof(ResetCounterAction), typeDiscriminator: "resetCounter")]
[JsonDerivedType(typeof(RaiseAlarmAction), typeDiscriminator: "raiseAlarm")]
[JsonDerivedType(typeof(AcknowledgeAlarmAction), typeDiscriminator: "ackAlarm")]
[JsonDerivedType(typeof(LogEventAction), typeDiscriminator: "logEvent")]
public abstract class LogicAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

public sealed class SetOutputAction : LogicAction
{
    public Guid VariableId { get; set; }
    public string Value { get; set; } = "true";
}

public sealed class SetMemoryAction : LogicAction
{
    public Guid VariableId { get; set; }
    public string Value { get; set; } = "true";
}

public sealed class ResetMemoryAction : LogicAction
{
    public Guid VariableId { get; set; }
}

public sealed class StartTimerAction : LogicAction
{
    public Guid TimerId { get; set; }
    public double PresetMs { get; set; }
}

public sealed class ResetTimerAction : LogicAction
{
    public Guid TimerId { get; set; }
}

public sealed class IncrementCounterAction : LogicAction
{
    public Guid CounterId { get; set; }
}

public sealed class ResetCounterAction : LogicAction
{
    public Guid CounterId { get; set; }
}

public sealed class RaiseAlarmAction : LogicAction
{
    public Guid AlarmDefinitionId { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class AcknowledgeAlarmAction : LogicAction
{
    public Guid AlarmDefinitionId { get; set; }
}

public sealed class LogEventAction : LogicAction
{
    public string Message { get; set; } = string.Empty;
}