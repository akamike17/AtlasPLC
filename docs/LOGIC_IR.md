# Logic IR (Modelo Intermedio de Lógica)

La fuente de verdad del sistema es este IR. La UI (futura vista Ladder/ST/FBD)
es solo una **representación** del IR, nunca la fuente.

## Estructura

```
LogicProgram
  ├─ Id, Name, Version, Enabled
  └─ Rules[]
       LogicRule
         ├─ Id, Name, Priority, Enabled
         ├─ Condition : ExpressionNode?
         ├─ Actions[] : LogicAction
         ├─ ElseActions[] : LogicAction
         ├─ SourceIntent, CreatedBy, Version
```

## Nodos de expresión

| Nodo | Descripción |
|------|-------------|
| `ConstantExpression` | valor literal tipado |
| `VariableExpression` | referencia a una variable |
| `NotExpression` | negación |
| `AndExpression` / `OrExpression` | conjunción / disyunción (cortocircuito) |
| `CompareExpression` | ==, !=, >, >=, <, <= |
| `ArithmeticExpression` | +, -, *, /, % |
| `EdgeExpression` | flanco ascendente/descendente |
| `TimerStateExpression` | Running/Done/Elapsed de un timer |
| `CounterStateExpression` | Current/Done de un contador |

## Acciones

`SetOutput`, `SetMemory`, `ResetMemory`, `StartTimer`, `ResetTimer`,
`IncrementCounter`, `ResetCounter`, `RaiseAlarm`, `AcknowledgeAlarm`, `LogEvent`.

## Serialización

Los tipos derivados se serializan/deserializan con discriminadores
`[JsonDerivedType]` (System.Text.Json), lo que permite persistir el IR en la
columna JSON de SQLite y en el formato `.atlasplc.json`.

## Determinismo

Mismo `InputSnapshot + ProgramVersion + RuntimeState` ⇒ mismo resultado.
No hay `DateTime` aleatorio ni IA durante la evaluación.