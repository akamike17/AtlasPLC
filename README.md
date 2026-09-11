# Atlas SoftPLC

Controlador lógico por software para operadores sin conocimientos de PLC.

La PC ejecuta el ciclo lógico. El operador expresa una intención en lenguaje
natural o mediante un asistente guiado; Atlas la convierte en un modelo lógico
interno determinista, la valida, la simula, y solo habilita una salida física
tras la activación explícita del modo físico.

> **Aviso de seguridad:** Atlas SoftPLC **no** es un Safety PLC, no es SIL-rated
> y no sustituye una función de seguridad física/certificada (E-Stop, relés de
> seguridad). Una señal marcada `SafetyCritical = true` no puede habilitarse
> silenciosamente y siempre muestra el aviso: *"Esta lógica no sustituye una
> función de seguridad física/certificada."*

## Stack

- C# / .NET 8, ASP.NET Core MVC
- Backend: BackgroundService (scan), System.Threading, Microsoft.Data.Sqlite
- Frontend: Razor `.cshtml`, HTML5, CSS, Bootstrap, JavaScript nativo, SignalR
- Protocolos: NModbus (TCP/RTU), OPC UA (fase posterior), MQTT (fase posterior)

## Estructura

```
src/
  AtlasSoftPlc.Domain/          modelo central (variables, IR, dispositivos)
  AtlasSoftPlc.Application/     servicios, validación, LogicBuilder, Explainer
  AtlasSoftPlc.Infrastructure/  persistencia SQLite + serialización JSON
  AtlasSoftPlc.Runtime/         motor de scan, expresiones, timers, arbitraje
  AtlasSoftPlc.Protocols.Modbus/ adaptador Modbus sobre NModbus
AtlasSoftPlc.Web/               MVC + SignalR + vistas
tests/
  AtlasSoftPlc.Domain.Tests
  AtlasSoftPlc.Application.Tests
  AtlasSoftPlc.Runtime.Tests
  AtlasSoftPlc.Protocols.Modbus.Tests
  AtlasSoftPlc.IntegrationTests
```

## Requisitos

- SDK .NET 8 (o superior). El SDK 10.0 genera soluciones `.slnx`.

## Compilar

```bash
dotnet build AtlasSoftPlc.slnx
```

## Ejecutar

```bash
dotnet run --project AtlasSoftPlc.Web.csproj
```

Abre `http://localhost:5035` (o el puerto indicado en `Properties/launchSettings.json`).

## Ejecutar pruebas

```bash
dotnet test AtlasSoftPlc.slnx
```

## Crear una simulación (flujo de aceptación)

1. Al abrir la app se crea automáticamente el proyecto demo **"Tanque de agua"**.
2. Ve a **Dashboard**. Verás las entradas (Nivel bajo, Nivel alto, Paro de
   emergencia) y la salida (Bomba).
3. Alterna **"Nivel bajo"** a ON → la bomba se enciende.
4. Alterna **"Nivel alto"** a ON → la bomba se apaga.
5. Alterna **"Paro de emergencia"** a ON → la bomba se apaga inmediatamente.

La vista **Simulación** muestra INPUTS / PROCESS / OUTPUTS y un timeline de
eventos. La vista **Diagnóstico** muestra métricas de scan.

## Flujo de una automatización

```
Texto del usuario
  → IntentParser / asistente guiado
  → AutomationIntent (DTO)
  → LogicBuilder
  → Logic IR (determinista, serializable)
  → ValidationService
  → Simulador
  → TestGenerator
  → Explainer (explicación humana)
  → Aprobación del usuario
  → Activación
```

La IA (si existe) solo propone `AutomationIntentProposal`; el motor determinista
decide si es válida. **Sin IA, todo lo demás funciona.**

## Cómo pasar a Shadow y Physical

- **Shadow Mode**: el runtime lee entradas físicas y calcula salidas propuestas,
  pero **no escribe** a los dispositivos. Muestra "Atlas habría encendido X".
- **Physical**: requiere activación explícita (rol Administrator) y el
  dispositivo pasa por interlock + failsafe antes de escribir.

## Cómo volver a modo seguro

```bash
# detener el runtime
curl -X POST http://localhost:5035/api/runtime/stop
```

Ante pérdida de dispositivo, excepción de driver, watchdog vencido o runtime
detenido, las salidas pasan a su `FailSafeValue` (por defecto `false` para
motores, bombas, válvulas normalmente cerradas).

## Documentación

Ver `docs/` para arquitectura, runtime, drivers, seguridad y pruebas.

## Licencias de terceros

Ver `THIRD_PARTY_NOTICES.md`.