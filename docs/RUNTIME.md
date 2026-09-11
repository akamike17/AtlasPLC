# Runtime

## Servicios

- `PlcRuntimeService` : `BackgroundService` — el loop de scan, single-writer.
- `ScanCoordinator` — coordina captura → ejecución → arbitraje por scan.
- `ExpressionEngine` — evalúa el IR; valida división por cero, overflow,
  conversión inválida, comparación de tipos, NaN/infinity.
- `TimerManager` / `CounterManager` — TON/TOF/TP y CTU/CTD/CTUD.
- `OutputArbiter` — resuelve conflictos por prioridad.
- `RuntimeStateStore` — snapshot observable (inmutable) del runtime.
- `WatchdogService` — heartbeat lógico.

## Ciclo

`PeriodicTimer` a `TargetScanPeriodMs = 50`. El tiempo se mide con `Stopwatch`
(tiempo monotónico), no se depende de `DateTime.Now` para los timers.

## Modos

- `Simulation` — drivers virtuales.
- `Shadow` — lee físicas, calcula salidas, **no escribe**.
- `Physical` — escribe previa activación explícita.

## Métricas

`LastScanMs`, `AverageScanMs`, `MaxScanMs`, `MinScanMs`, `Overruns`,
`TotalScans`, `LastCompletedUtc`. Nunca se ejecutan dos scans simultáneos.

## Concurrencia

El estado de ejecución solo lo muta el loop. UI/config envían
`RuntimeCommand` por `Channel<RuntimeCommand>`. Ejemplos: `ActivateProgramCommand`,
`PauseCommand`, `SetManualInputCommand`, `ForceOutputCommand`,
`ClearForceCommand`, `SetRuntimeModeCommand`.

## Fallos

No hay `catch { }`. Cada excepción se clasifica, registra con contexto y deja
driver/runtime en estado coherente. Ante fallo repetido de un driver, se activa
circuit breaker (`RetryWaiting` con backoff).