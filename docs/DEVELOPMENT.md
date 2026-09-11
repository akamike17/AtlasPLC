# Guía de desarrollo

## Convenciones

- **Solidaridad con el runtime**: nunca usar `catch { }` vacío; clasificar cada
  excepción.
- **Determinismo**: no usar `DateTime.Now` en la lógica de scan; usar `Stopwatch`
  o `TimeProvider`.
- **Responsabilidades claras**: no crear `TodoService`, `Helpers.cs`, `Utils.cs`.
- **Controllers delgados**: nada de lógica industrial en MVC controllers.
- **Single-writer**: el estado del runtime solo lo muta el loop.

## Flujo de trabajo

```
READ → PLAN → IMPLEMENT → BUILD → TEST → FIX → VERIFY → NEXT
```

Ejecutar `dotnet test AtlasSoftPlc.slnx` tras cada cambio funcional.

## Agregar un driver

1. Implementar `IDeviceDriver` en el proyecto de protocolo correspondiente.
2. Declarar `DriverCapabilities`.
3. Convertir `protocolo → VariableId` vía `TagBinding`.
4. Crear mock/fake (`FakeDeviceDriver`, `FaultingDeviceDriver`, etc.).
5. Pruebas contra simulador/referencia, nunca "de memoria".

## Agregar una validación

Implementar `IValidationRule` y registrarla en `ValidationService` (Program.cs).

## Persistencia

SQLite. El scan **nunca** escribe a la base de datos. La persistencia de
retentivas usa buffer + dirty tracking + checkpoints + graceful shutdown.