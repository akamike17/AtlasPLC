# Arquitectura Atlas SoftPLC

## Principio

El operador expresa intención; Atlas la convierte en un **Logic Intermediate
Model (IR)** determinista y auditable. Nunca se ejecuta texto del usuario, nunca
se usa `eval`, nunca se compila texto arbitrario.

## Capas

```
┌────────────── AtlasSoftPlc.Web (MVC + SignalR) ──────────────┐
│  Controllers delgados, vistas Razor, hub de tiempo real      │
└──────────────────────┬───────────────────────────────────────┘
                       │ (solo comandos por Channel<T>)
┌──────────────────────▼───────────────────────────────────────┐
│ AtlasSoftPlc.Application (servicios + validación + IR)        │
│  LogicBuilder, ValidationService, Explainer, TestGenerator   │
└──────────────────────┬───────────────────────────────────────┘
                       │
┌──────────────────────▼───────────────────────────────────────┐
│ AtlasSoftPlc.Runtime (motor de scan, single-writer)           │
│  ScanCoordinator → ExpressionEngine → OutputArbiter → failsafe│
└──────────────────────┬───────────────────────────────────────┘
                       │
┌──────────────────────▼───────────────────────────────────────┐
│ AtlasSoftPlc.Domain (modelo: variables, IR, dispositivos)     │
└──────────────────────┬───────────────────────────────────────┘
                       │
┌──────────────────────▼───────────────────────────────────────┐
│ Device drivers (Virtual, Modbus Tcp/Rtu, OPC UA, MQTT)        │
│ AtlasSoftPlc.Infrastructure (SQLite, JSON)                    │
└───────────────────────────────────────────────────────────────┘
```

## Flujo de un scan

```
1. capturar entradas → congelar Input Image
2. ejecutar lógica (IR) sobre snapshots inmutables
3. generar OutputProposal
4. OutputArbiter (resuelve conflictos por prioridad)
5. interlock + failsafe
6. Output Image
7. escribir salidas (solo a través del driver)
8. publicar diagnóstico + métricas
```

## Decisiones clave

- **Single-writer**: el runtime es el único que muta el estado de ejecución.
  La UI envía comandos por `Channel<RuntimeCommand>`, no accede al estado.
- **Determinismo**: mismo `InputSnapshot + ProgramVersion + RuntimeState`
  produce el mismo resultado. No se usa IA ni `DateTime` aleatorio en el scan.
- **Atomic swap**: activar una versión nueva construye una instancia validada
  y la intercambia con `Interlocked`-equivalente; nunca deja el runtime a medias.
- **Failsafe por defecto false**: toda salida física define `FailSafeValue`.
- **IA fuera del scan**: el proveedor de IA devuelve `AutomationIntentProposal`;
  nunca escribe outputs, no activa programas, no desactiva interlocks.

## Modelo intermedio (IR)

- `LogicProgram` → `LogicRule[]` → `Condition` (árbol de `ExpressionNode`) + `Actions[]`.
- Tipos de expresión: Constant, Variable, Not, And, Or, Compare, Arithmetic,
  Edge (rising/falling), TimerState, CounterState.
- Acciones: SetOutput, SetMemory, ResetMemory, StartTimer, ResetTimer,
  IncrementCounter, ResetCounter, RaiseAlarm, AcknowledgeAlarm, LogEvent.

## Ver también

- [RUNTIME.md](RUNTIME.md)
- [LOGIC_IR.md](LOGIC_IR.md)
- [DEVICE_DRIVERS.md](DEVICE_DRIVERS.md)
- [SECURITY.md](SECURITY.md)
- [TESTING.md](TESTING.md)