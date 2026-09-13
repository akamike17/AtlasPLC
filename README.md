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
>
> `SafetyCritical` solo activa análisis y diagnósticos de ingeniería. No certifica
> seguridad funcional, SIL o PL, y no sustituye hardware de seguridad, relés de
> seguridad ni un Safety PLC certificado.

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

> Credenciales de desarrollo: `admin` / `AtlasDemo!2026` (solo en `Development`;
> ver `appsettings.Development.json`). En producción las contraseñas vienen de
> variables de entorno (ver `## Despliegue en producción`).

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

## Despliegue en producción

Lista de verificación antes de exponer el sistema fuera de una red aislada:

1. **HTTPS obligatorio.** Configura TLS y `ASPNETCORE_AllowedHosts` con el FQDN real
   (no `*`). La cookie de auth se emite con `Secure` en producción automáticamente.
2. **Contraseñas reales.** Define `Auth__SeedPassword` y `Auth__Seed__<usuario>=<rol>`
   como variables de entorno (ver `.env.example`). Nunca commitees `.env`.
3. **Migraciones.** El esquema se versiona con `SchemaMigrator`; cualquier cambio de
   tabla requiere una migración nueva (no edites el SQL de migraciones ya aplicadas).
4. **Observabilidad.** `/health` (liveness) y `/health/ready` (readiness) sin auth.
   Logs estructurados (Serilog) en `%LocalAppData%/AtlasSoftPlc/logs/`.
5. **Retención.** Timeline acotado en memoria (1000). Historian podable con
   `HistorianService.PruneAsync(días)` — prográmalo (cron/hosted service).
6. **Backup.** SQLite es un único archivo; respáldalo (`%LocalAppData%/AtlasSoftPlc/atlas.db`).

> **Seguridad residual aceptada (MVP):** auth en memoria (lockout/rate-limit) no persiste
> entre reinicios; auth por cookie sin refresh-token; no hay OPC UA/MQTT todavía.
> Para multi-nodo o exposición pública, migra el store de usuarios y la BD a un motor
> con HA y usa un IdP (OIDC/OAuth2).

## Licencias de terceros

Ver `THIRD_PARTY_NOTICES.md`.
