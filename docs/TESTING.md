# Pruebas

## Cobertura por proyecto

| Proyecto | Pruebas |
|----------|---------|
| Domain.Tests | PlcValue, TimerState (TON/TOF/TP), CounterState (CTU/CTD) |
| Runtime.Tests | ExpressionEngine (and/or/not/compare/div0/overflow), ScanCoordinator (determinismo, conflictos, interlocks, failsafe), timers/counters vía scan |
| Application.Tests | LogicBuilder, ValidationService, serialización IR, IntentParser |
| Protocols.Modbus.Tests | coil/register read/write, endianness, float, timeout, unit id, bulk, reconnect |
| IntegrationTests | escenarios E2E (motor, tanque, conflicto, stale) |

## Ejecutar

```bash
dotnet test AtlasSoftPlc.slnx
```

## Escenarios E2E (sección 72)

1. Motor básico (Start + DoorClosed + !Alarm → Motor)
2. Timer (Sensor → TON → Fan)
3. Tanque (Low → Fill ON; High → Fill OFF)
4. Conflicto (dos escritores) → resuelto por prioridad/failsafe
5. Pérdida de dispositivo → failsafe
6. Rollback de versión
7. Shadow mode
8. Contador retentivo

## Producto objetivo

Criterio "operador que no sabe PLC": crear una automatización sin introducir
coil/register/rung/ST/dirección/bit. Solo preguntar qué sensor/salida usar.