# ATLAS PLC — AUDITORÍA QUIRÚRGICA DEL RUNTIME

**Repositorio:** `akamike17/AtlasPLC`  
**Base auditada:** `46e7c1aa04e5af9b2fab331b3a8fd09dec2e159e`  
**Objetivo:** cerrar errores funcionales y de seguridad operacional del núcleo PLC. No rehacer arquitectura ni UI.

## Estado de las correcciones anteriores

Las cinco correcciones pedidas en la auditoría anterior aparecen implementadas en el commit auditado:

- Lockout: ya no bloquea desde el primer fallo.
- `ProjectId`: Variables y LogicPrograms ya persisten su proyecto.
- AlarmDefinition: ya tiene persistencia real.
- Outputs UI: ya toma el estado del runtime.
- AuditEvents: ya ordena por `TimestampUtc`.

**No revertir estas correcciones.**

---

# BLOQUE CRÍTICO — RUNTIME PLC

## P0-1 — STOP / PAUSE / FAULT / SHUTDOWN NO LLEVAN LAS SALIDAS A FAILSAFE

**Archivo principal:**  
`src/AtlasSoftPlc.Runtime/Hosting/PlcRuntimeService.cs`

Actualmente `PauseCommand`, `StopCommand`, una excepción durante `RunScan()` y `ShutdownAsync()` cambian el estado, pero no existe una transición explícita y atómica de todas las salidas a sus valores failsafe.

Esto permite que el snapshot conserve el último estado lógico conocido; una salida previamente ON puede seguir apareciendo ON después de STOP/Fault.

### Corregir

Crear una única rutina, por ejemplo:

`ApplyFailsafeOutputs(reason)`

Debe:

1. recorrer **todas** las variables Output;
2. obtener su failsafe configurado;
3. si falta failsafe, usar una política explícita y validada, nunca implícita;
4. actualizar `_lastOutputs` y `RuntimeStateStore`;
5. notificar cambios;
6. ser usada por `Stop`, `Pause`, `Fault`, shutdown y watchdog/fallo crítico;
7. no depender de que exista una propuesta de lógica en ese scan.

### Pruebas obligatorias

- Output ON → Stop → output = failsafe.
- Output ON → Pause → output = failsafe.
- Output ON → excepción de scan → `Faulted` + failsafe.
- Output ON → cancelación/shutdown → failsafe.
- múltiples outputs con failsafe distintos.

---

## P0-2 — ERROR AL EVALUAR UN INTERLOCK NO ES FAIL-CLOSED

**Archivo:**  
`src/AtlasSoftPlc.Runtime/Engine/ScanCoordinator.cs`

En `EvaluateInterlocks()`, si la expresión falla:

`active[interlock.Id] = false`

Eso desactiva el interlock precisamente cuando no pudo comprobarse su condición. El comentario indica que el failsafe se manejaría después, pero `OutputArbiter` sólo aplica failsafe por conflicto; el error del interlock no fuerza failsafe.

### Corregir

Un interlock inválido/no evaluable debe tener política segura explícita.

Para outputs afectados:

**error de evaluación del interlock => SafeValue/failsafe**, nunca continuar como si el interlock estuviera inactivo.

No ocultar el error: conservarlo en `ScanResult.Errors` y trazabilidad.

### Pruebas

- condición de interlock válida/false → control normal;
- válida/true → SafeValue;
- expresión inválida → SafeValue/failsafe + error;
- variable faltante/tipo inválido → fail-closed.

---

## P0-3 — FORCE OUTPUT ESTÁ IMPLEMENTADO A MEDIAS Y ACTUALMENTE NO FUERZA NADA

**Archivos:**

- `src/AtlasSoftPlc.Runtime/Hosting/RuntimeState.cs`
- `src/AtlasSoftPlc.Runtime/Hosting/PlcRuntimeService.cs`
- motor/arbitraje donde corresponda.

`ForceOutputCommand` contiene `PlcValue Value` y `ExpiresAfter`, pero `ProcessCommand()` guarda únicamente:

`_forcedOutputs[f.VariableId] = true`

Se pierde el valor solicitado, no se usa `ExpiresAfter` y `_forcedOutputs` no participa en `RunScan()`/arbitraje.

### Corregir

Implementar force completo o eliminar/deshabilitar explícitamente la función hasta que sea segura. Si se implementa:

- almacenar VariableId + valor + fecha de expiración + metadata necesaria;
- validar que la variable exista y sea Output;
- validar tipo;
- integrar el force al arbitraje con prioridad explícita;
- definir que **interlock/failsafe de seguridad tiene prioridad sobre force**;
- `ClearForce` debe restaurar control automático en el siguiente scan;
- expiración automática;
- limpiar forces en instalación/cambio de programa, Stop/Fault según política documentada;
- auditar force/clear/expire.

### Pruebas

Force ON/OFF, clear, expiración, tipo inválido, output inexistente, force contra interlock activo y force durante Stop/Fault.

---

## P0-4 — WATCHDOG NO PROTEGE EL SCAN

**Archivos:**

- `src/AtlasSoftPlc.Runtime/Hosting/RuntimeState.cs`
- `src/AtlasSoftPlc.Runtime/Hosting/PlcRuntimeService.cs`

El runtime llama `_watchdog.Heartbeat()` en cada iteración del loop incluso estando `Stopped`. Además `IsAlive` no participa en una transición operacional ni aplica failsafe.

Por lo tanto hoy el watchdog es principalmente informativo y no una protección real del scan.

### Corregir

Definir claramente qué vigila:

- heartbeat de **scan completado correctamente**, no simplemente loop vivo;
- detectar scan bloqueado/atrasado;
- al exceder timeout en Running: estado Faulted + failsafe;
- no reportar salud falsa cuando no se ejecutan scans;
- exponer diagnóstico: último heartbeat, timeout y estado.

Evitar un watchdog que dependa exclusivamente del mismo hilo que podría quedar bloqueado.

### Pruebas

scan normal, scan lento, timeout, runtime detenido y recuperación controlada.

---

## P1-1 — OUTPUT SIN PROPUESTA DESAPARECE DEL SNAPSHOT

**Archivos:**

- `src/AtlasSoftPlc.Runtime/Engine/OutputArbiter.cs`
- `src/AtlasSoftPlc.Runtime/Engine/ScanCoordinator.cs`

El arbitraje agrupa únicamente outputs que recibieron `OutputProposal`. Si una salida definida no recibe propuesta en un scan, no se genera decisión para ella.

### Corregir

Definir semántica PLC explícita para **cada output en cada scan**.

Ninguna salida física debe quedar sin decisión. Según diseño aprobado:

- valor calculado,
- valor retenido explícitamente, o
- failsafe.

No permitir que “no hubo propuesta” sea una cuarta semántica accidental.

Agregar prueba con 2 outputs donde sólo uno recibe propuesta.

---

## P1-2 — CONFLICTO DE IGUAL PRIORIDAD DEBE SER DETERMINISTA Y SEGURO

**Archivo:**  
`src/AtlasSoftPlc.Runtime/Engine/OutputArbiter.cs`

Se detecta `Conflicted` cuando dos propuestas tienen la prioridad máxima. Si existe failsafe se usa, pero si no existe se conserva `top`.

### Corregir

Un conflicto irresoluble no debe depender del orden incidental de enumeración.

- conflicto de igual prioridad => failsafe o error de configuración que impida activar el programa;
- nunca “primero de la lista gana”;
- registrar las reglas competidoras.

Pruebas invirtiendo el orden de las reglas deben producir exactamente el mismo resultado.

---

## P1-3 — ERRORES DE PARSEO DE ACCIONES SE CONVIERTEN SILENCIOSAMENTE EN STRING

**Archivo:**  
`src/AtlasSoftPlc.Runtime/Engine/LogicExecutor.cs`

`ParseTyped()` captura cualquier excepción y devuelve `PlcValue.String(value)`.

En runtime industrial esto puede esconder una configuración inválida hasta una etapa posterior.

### Corregir

La configuración debe ser validada antes de activarse. Si aun así llega un valor imposible al executor:

- devolver error explícito;
- no generar una propuesta inválida;
- no convertir silenciosamente a String;
- aplicar política segura al output afectado.

Pruebas para Bool, enteros, overflow, decimal y tipos incompatibles.

---

# VALIDACIÓN DE REGRESIÓN DE LA CORRECCIÓN ANTERIOR

Agregar/conservar pruebas que demuestren:

1. Auth: intentos 1–4 incorrectos = `InvalidCredentials`; intento 5 activa lockout.
2. Variable y LogicProgram sobreviven cierre/reapertura real de SQLite y sólo aparecen en su `ProjectId`.
3. AlarmDefinition sobrevive reapertura de SQLite.
4. AuditEvent devuelve orden cronológico real.
5. Output UI coincide con `RuntimeStateStore`.

**Importante:** probar reapertura con una instancia nueva de repositorio/store, no sólo leer inmediatamente desde el mismo objeto.

---

# MIGRACIONES SQLITE

Revisar especialmente `SchemaMigrator` v1→v2→v3 con una base **existente**, no sólo base vacía.

Verificar:

- migración desde versión 1;
- migración desde versión 2;
- ejecución sobre versión 3;
- datos previos preservados;
- columnas nuevas utilizables;
- ningún `ALTER TABLE` repetido;
- timestamps antiguos NULL no rompen consultas.

---

# CIERRE OBLIGATORIO

No declarar terminado por número de tests.

Ejecutar, en este orden:

```powershell
dotnet restore AtlasSoftPlc.slnx
dotnet build AtlasSoftPlc.slnx -c Release --no-restore
dotnet test AtlasSoftPlc.slnx -c Release --no-build
dotnet publish AtlasSoftPlc.Web.csproj -c Release --no-restore -o artifacts/publish
```

Resultado requerido:

- **0 errores**
- **0 warnings del proyecto** o documentar justificadamente cualquier warning externo inevitable
- **100% tests aprobados**
- publish exitoso
- sin `NotImplementedException`
- sin stubs/no-op en rutas operacionales
- sin hardcodes que falsifiquen estados
- sin tests modificados únicamente para aceptar comportamiento incorrecto

## Regla de trabajo

Trabaja `read → write → verify → test`.

No hagas refactor cosmético. No cambies arquitectura sin necesidad. Corrige primero P0, después P1. Cada bug debe quedar cubierto por una prueba que falle antes de la corrección y pase después.

Al finalizar entrega un resumen corto con:

- archivos modificados;
- causa raíz de cada P0/P1;
- pruebas nuevas;
- resultado exacto de build/test/publish;
- cualquier riesgo residual real.

**No declares AtlasPLC listo para uso físico sólo porque el software pasa pruebas. Sigue siendo un SoftPLC y no sustituye un Safety PLC ni funciones físicas certificadas de seguridad.**
