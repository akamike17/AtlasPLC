# ATLAS SOFTPLC — ESPECIFICACIÓN MAESTRA DE IMPLEMENTACIÓN
## Controlador lógico por software para operadores sin conocimientos de PLC

**Documento para ejecución autónoma por agente de código (DeepSeek u otro).**  
**Estado:** especificación de arquitectura + implementación + validación + pruebas.  
**Prioridad:** construir un MVP real, estable y extensible; no una maqueta visual.

---

# 0. MISIÓN

Construir **Atlas SoftPLC**, un controlador lógico basado en PC en el que:

- La **PC ejecuta el ciclo lógico**.
- El operador **NO necesita saber PLC, Ladder, Structured Text, direcciones, registros ni protocolos industriales**.
- El operador expresa una intención en lenguaje natural o mediante un asistente guiado.
- El sistema convierte esa intención en un **modelo lógico interno determinista**.
- Antes de habilitar una salida física, el sistema:
  1. interpreta,
  2. valida,
  3. detecta contradicciones,
  4. verifica tipos/rangos,
  5. comprueba dependencias,
  6. simula,
  7. ejecuta pruebas,
  8. muestra al usuario qué ocurrirá,
  9. exige activación explícita del modo físico.
- El mismo proyecto debe poder operar:
  - 100 % simulado,
  - con Modbus TCP,
  - con Modbus RTU,
  - mediante OPC UA,
  - con ESP32/Arduino usados como I/O remoto,
  - posteriormente con Siemens,
  - posteriormente con Allen-Bradley/Rockwell,
  - posteriormente con MQTT/Sparkplug para integración IIoT.

**NO construir un clon de TIA Portal, Studio 5000 o CODESYS.**  
La ventaja del producto debe ser ocultar la complejidad industrial al operador.

---

# 1. PRINCIPIO CENTRAL

El usuario nunca debe verse obligado a escribir:

```text
I0.0
Q0.3
M10.2
DB4.DBX0.1
40001
%IX0.0
TON(...)
```

Debe poder indicar cosas como:

> Cuando oprima Inicio, si la puerta está cerrada y no existe una alarma, enciende el motor.  
> Si se abre la puerta, apágalo inmediatamente.  
> Si el motor trabaja más de 20 minutos, deténlo durante 5 minutos.  
> Si presiono Paro de Emergencia, ninguna orden deberá poder encender el motor.

Atlas traduce eso a un modelo interno explícito y auditable.

---

# 2. REGLAS NO NEGOCIABLES DE TECNOLOGÍA

## Backend

Usar:

- C#
- .NET 8
- ASP.NET Core MVC
- Hosted Services / BackgroundService para tareas no UI
- Dependency Injection nativa de ASP.NET Core
- `System.Threading`
- `System.IO.Ports` únicamente cuando sea necesario para serial
- `System.Net` / sockets cuando sean suficientes
- SQLite inicialmente
- Microsoft.Data.Sqlite
- EF Core únicamente si aporta mantenibilidad real; evitar una arquitectura inflada para tablas triviales

## Frontend

Usar exclusivamente:

- Razor Views `.cshtml`
- HTML5
- CSS
- Bootstrap
- JavaScript moderno nativo
- `fetch`
- SignalR si aporta valor para variables en tiempo real

## PROHIBIDO

No usar:

- Windows Forms
- WPF
- MAUI
- Blazor
- React
- Vue
- Angular
- Svelte
- Node como backend
- Electron
- frameworks JS adicionales salvo justificación técnica crítica

## Librerías permitidas si son gratuitas y mantenibles

Prioridad:

1. `NModbus` — Modbus TCP/RTU
2. `OPCFoundation.NetStandard.Opc.Ua.*` — OPC UA
3. `MQTTnet` — MQTT
4. `S7.Net+` — Siemens, fase posterior
5. `libplctag.NET` — Rockwell/Allen-Bradley, fase posterior

No introducir una dependencia si el BCL de .NET resuelve el problema claramente.

---

# 3. ESTÁNDARES Y REFERENCIAS DE DISEÑO

El software NO necesita certificarse inicialmente, pero debe diseñarse con conceptos compatibles con:

## IEC 61131-3

Modelo conceptual basado en:

- Structured Text (ST)
- Ladder Diagram (LD)
- Function Block Diagram (FBD)
- Sequential Function Chart (SFC)
- programas,
- funciones,
- function blocks,
- variables tipadas,
- configuración y ejecución cíclica.

**Importante:** IEC 61131-3:2025 ya presenta ST, LD, FBD y SFC como lenguajes/elementos principales.  
No implementar Instruction List como lenguaje de usuario.

## OPC UA

Usarlo como **capa universal semántica/interoperable**, no como motor lógico.

Atlas podrá actuar como:

- OPC UA Server,
- OPC UA Client,
- ambas funciones simultáneamente.

Cada variable expuesta deberá tener:

- Node/Id interno estable,
- nombre,
- tipo,
- unidad,
- calidad,
- timestamp,
- acceso read/write,
- origen,
- descripción,
- estado.

## Modbus

Implementar primero:

- Modbus TCP Client
- Modbus TCP Server
- Modbus RTU Client
- Modbus RTU Server si la librería/arquitectura lo permite limpiamente

Soportar conceptos:

- coils,
- discrete inputs,
- input registers,
- holding registers.

Evitar que el usuario final tenga que conocer las direcciones.

## EtherNet/IP / CIP

No implementar CIP manualmente.

Para Rockwell usar en fase posterior:

- `libplctag.NET`

## Siemens

No reimplementar ISO-on-TCP/S7 desde cero.

En fase posterior usar:

- `S7.Net+`

## MQTT / Sparkplug

MQTT es telemetría/integración, **no debe estar en el scan crítico**.

Preparar una capa para Sparkplug B futura.

## Seguridad OT

Tomar como referencia:

- NIST SP 800-82 Rev. 3
- IEC 62443 conceptualmente
- autenticación/certificados OPC UA
- zonas de red
- mínimo privilegio
- separación configuración/operación

---

# 4. LÍMITE DE SEGURIDAD DEL PRODUCTO

Atlas SoftPLC inicial NO deberá presentarse como:

- Safety PLC,
- SIL-rated,
- controlador certificado de seguridad funcional,
- sustituto certificado para E-Stop,
- sustituto para relés de seguridad,
- controlador hard-real-time.

Windows no es hard-real-time.

## Regla de seguridad

Una señal marcada por el usuario como:

```text
SafetyCritical = true
```

NO puede habilitarse silenciosamente.

Debe aparecer una advertencia:

```text
Esta lógica no sustituye una función de seguridad física/certificada.
```

## Emergencia

Un E-Stop físico verdadero debe cortar energía/control seguro mediante hardware apropiado.

Atlas puede:

- supervisarlo,
- registrar su estado,
- reaccionar por software,

pero nunca afirmar que reemplaza el circuito físico de seguridad.

---

# 5. ARQUITECTURA GENERAL

```text
┌────────────────────────────────────────────────────────────┐
│                        OPERADOR                            │
│ "Si pasa X, haz Y, excepto cuando Z"                      │
└───────────────────────────┬────────────────────────────────┘
                            │
                            ▼
┌────────────────────────────────────────────────────────────┐
│                  INTENT / RULE BUILDER                     │
│ Parser guiado + asistente + lenguaje natural              │
└───────────────────────────┬────────────────────────────────┘
                            │
                            ▼
┌────────────────────────────────────────────────────────────┐
│                    VALIDATION ENGINE                       │
│ Tipos · ciclos · contradicciones · seguridad · límites    │
└───────────────────────────┬────────────────────────────────┘
                            │
                            ▼
┌────────────────────────────────────────────────────────────┐
│                   LOGIC INTERMEDIATE MODEL                 │
│ AST/IR determinista, versionado y serializable             │
└───────────────────────────┬────────────────────────────────┘
                            │
          ┌─────────────────┼───────────────────┐
          ▼                 ▼                   ▼
     SIMULATOR        SCAN ENGINE          EXPLAINER
          │                 │                   │
          │                 ▼                   │
          │       ┌──────────────────┐          │
          │       │ INPUT IMAGE      │          │
          │       └────────┬─────────┘          │
          │                ▼                    │
          │       EXECUTE LOGIC IR              │
          │                ▼                    │
          │       ┌──────────────────┐          │
          │       │ OUTPUT IMAGE     │          │
          │       └────────┬─────────┘          │
          │                ▼                    │
          │        OUTPUT ARBITER               │
          │                ▼                    │
          └──────────► DEVICE ABSTRACTION ◄─────┘
                           │
        ┌──────────────────┼──────────────────┐
        ▼                  ▼                  ▼
     Modbus              OPC UA             Virtual
        │                  │                  I/O
   ┌────┴────┐        ┌────┴─────┐
 ESP32    PLC real   SCADA      MES/HMI
```

---

# 6. SOLUCIÓN Y PROYECTOS

Crear solución:

```text
AtlasSoftPlc.sln

src/
  AtlasSoftPlc.Domain/
  AtlasSoftPlc.Application/
  AtlasSoftPlc.Infrastructure/
  AtlasSoftPlc.Runtime/
  AtlasSoftPlc.Protocols.Modbus/
  AtlasSoftPlc.Protocols.OpcUa/
  AtlasSoftPlc.Protocols.Mqtt/
  AtlasSoftPlc.Web/

tests/
  AtlasSoftPlc.Domain.Tests/
  AtlasSoftPlc.Application.Tests/
  AtlasSoftPlc.Runtime.Tests/
  AtlasSoftPlc.Protocols.Modbus.Tests/
  AtlasSoftPlc.IntegrationTests/
  AtlasSoftPlc.EndToEnd.Tests/
```

No crear proyectos vacíos sin justificación.

---

# 7. DOMAIN — MODELO CENTRAL

## Variable

```text
VariableDefinition
- Id : Guid
- Key : string
- DisplayName : string
- Description : string
- DataType : PlcDataType
- Direction : Input | Output | Memory | Internal
- EngineeringUnit : string?
- MinValue : decimal?
- MaxValue : decimal?
- DefaultValue
- Retentive : bool
- SafetyCritical : bool
- ReadOnly : bool
- CreatedUtc
- UpdatedUtc
```

Tipos iniciales:

```text
Bool
Int16
UInt16
Int32
UInt32
Int64
UInt64
Float
Double
Decimal
String
DateTime
TimeSpan
```

Internamente evitar `object` indiscriminado.

Crear un `PlcValue`/union tipado o estrategia equivalente.

## Runtime value

```text
RuntimeValue
- VariableId
- Value
- Quality
- TimestampUtc
- Source
- SequenceNumber
```

Quality:

```text
Good
Uncertain
Bad
Disconnected
Stale
Forced
Simulated
```

---

# 8. ABSTRACCIÓN DE DISPOSITIVOS

Crear:

```csharp
public interface IDeviceDriver
{
    string DriverId { get; }
    Task<DeviceConnectionResult> ConnectAsync(...);
    Task DisconnectAsync(...);
    Task<IReadOnlyDictionary<Guid, PlcValue>> ReadInputsAsync(...);
    Task<WriteResult> WriteOutputsAsync(...);
    Task<DeviceHealth> GetHealthAsync(...);
}
```

No contaminar el motor lógico con detalles Modbus/S7/etc.

Cada driver debe convertir:

```text
protocolo industrial
      ↓
VariableId Atlas
```

mediante `TagBinding`.

## TagBinding

```text
TagBinding
- Id
- VariableId
- DeviceId
- Protocol
- Address
- DataType
- ByteOrder
- BitIndex?
- Scale
- Offset
- ReadWriteMode
- PollInterval
- Timeout
- RetryPolicy
```

UI traduce direcciones a nombres comprensibles.

---

# 9. DISPOSITIVO VIRTUAL

Debe existir desde el primer día.

Tipos de I/O virtual:

- interruptor
- pulsador
- sensor digital
- sensor analógico
- potenciómetro
- temperatura
- presión
- nivel
- motor
- válvula
- lámpara
- relé
- contador
- encoder simplificado

El dispositivo virtual permite probar el sistema sin hardware.

---

# 10. MOTOR DE SCAN

## Objetivo

Imitar conceptualmente el scan de PLC:

```text
1. capturar entradas
2. congelar Input Image
3. ejecutar lógica
4. generar Output Image
5. validar/arbitrar salidas
6. escribir salidas
7. publicar diagnóstico
8. registrar métricas
9. siguiente ciclo
```

## Importante

NO permitir que una regla escriba directamente al driver.

Toda escritura pasa:

```text
Logic Engine
    ↓
Proposed Output
    ↓
Output Arbiter
    ↓
Safety / Interlock Validation
    ↓
Output Image
    ↓
Device Driver
```

## Servicio

Crear:

```text
PlcRuntimeService : BackgroundService
```

No ejecutar el scan desde un Controller MVC.

## Tiempos

Configuración inicial:

```text
TargetScanPeriodMs = 50
```

Permitidos en modo experimental:

```text
10, 20, 50, 100, 250, 500, 1000 ms
```

Nunca prometer exactitud hard-real-time.

Métricas:

```text
LastScanMs
AverageScanMs
MaxScanMs
MinScanMs
Overruns
TotalScans
LastCompletedUtc
InputsReadMs
LogicExecutionMs
OutputsWriteMs
```

## Overrun

Si:

```text
execution > TargetScanPeriod
```

registrar `ScanOverrun`.

Nunca ejecutar dos scans simultáneos.

---

# 11. SNAPSHOT / IMÁGENES DE PROCESO

Crear estructuras inmutables por scan:

```text
InputSnapshot
MemorySnapshot
OutputProposal
OutputSnapshot
```

Una regla nunca debe leer una entrada que cambió a mitad de ciclo.

---

# 12. MODELO INTERMEDIO DE LÓGICA — IR

No ejecutar directamente texto del usuario.

Crear IR:

```text
LogicProgram
  Id
  Name
  Version
  Enabled
  Rules[]
```

Cada regla:

```text
LogicRule
- Id
- Name
- Priority
- Enabled
- Condition
- Actions[]
- ElseActions[]
- SourceIntent
- CreatedBy
- Version
```

## Expression nodes

```text
ConstantExpression
VariableExpression
NotExpression
AndExpression
OrExpression
CompareExpression
ArithmeticExpression
EdgeExpression
TimerStateExpression
CounterStateExpression
```

## Actions

```text
SetOutputAction
SetMemoryAction
ResetMemoryAction
StartTimerAction
ResetTimerAction
IncrementCounterAction
ResetCounterAction
RaiseAlarmAction
AcknowledgeAlarmAction
LogEventAction
```

No usar `eval`.

No compilar texto arbitrario recibido del usuario.

---

# 13. MOTOR DE EXPRESIONES

Operadores:

```text
AND
OR
NOT

==
!=
>
>=
<
<=

+
-
*
/
%

rising-edge
falling-edge
```

Validaciones:

- división por cero
- overflow
- conversión inválida
- comparación de tipos incompatibles
- NaN/infinity
- rango de ingeniería

---

# 14. TEMPORIZADORES

Implementar bloques:

```text
TON
TOF
TP
```

Campos:

```text
TimerId
Preset
Elapsed
Input
Output
Running
Done
LastUpdatedUtc
```

Los temporizadores se actualizan desde tiempo monotónico.

No depender exclusivamente de `DateTime.Now`.

Usar `TimeProvider` / `Stopwatch` según corresponda.

---

# 15. CONTADORES

Implementar:

```text
CTU
CTD
CTUD
```

Soportar:

- preset,
- current,
- done,
- reset,
- edge-triggered counting.

---

# 16. MEMORIA RETENTIVA

Variables `Retentive=true` sobreviven reinicio.

Persistir mediante almacenamiento transaccional.

NO escribir SQLite en cada scan.

Usar:

- buffer,
- dirty tracking,
- checkpoints,
- graceful shutdown.

---

# 17. RESOLUCIÓN DE CONFLICTOS DE SALIDA

Caso:

```text
Regla A -> Motor = ON
Regla B -> Motor = OFF
```

No usar “la última gana” silenciosamente.

Implementar `OutputArbiter`.

Prioridad conceptual:

```text
1 Emergency/Safety interlock
2 Manual forced-safe command
3 Fault handling
4 Interlock
5 Automatic control
6 Manual normal command
7 Default/failsafe
```

Conflicto no resoluble:

```text
OutputConflict
```

La salida deberá tomar su `FailSafeValue`.

---

# 18. FAIL-SAFE

Toda salida física debe definir:

```text
FailSafeValue
```

Ejemplo:

```text
Motor = false
Heater = false
ValveNormallyClosed = false
```

Ante:

- pérdida de dispositivo,
- excepción de driver,
- lógica inválida,
- corrupción de configuración,
- runtime detenido,
- watchdog vencido,

se intentará pasar a estado seguro configurado, sin prometer seguridad certificada.

---

# 19. WATCHDOG

Crear watchdog lógico.

Estados:

```text
Starting
Running
Degraded
Faulted
Stopped
Emergency
```

Registrar heartbeat interno.

UI debe visualizar inmediatamente si el scan está detenido.

---

# 20. NATURAL LANGUAGE / ZERO KNOWLEDGE OPERATOR

La IA NO controla hardware directamente.

Flujo obligatorio:

```text
Texto usuario
  ↓
IntentParser
  ↓
AutomationIntent DTO
  ↓
Normalizer
  ↓
LogicBuilder
  ↓
Logic IR
  ↓
Validator
  ↓
Simulator
  ↓
Test Generator
  ↓
Explanation
  ↓
User approval
  ↓
Activate
```

## AutomationIntent

Ejemplo:

```json
{
  "trigger": "StartButton becomes true",
  "conditions": [
    "DoorClosed == true",
    "AlarmActive == false"
  ],
  "actions": [
    "Motor = true"
  ],
  "stopConditions": [
    "DoorClosed == false",
    "StopButton == true"
  ]
}
```

Si existe proveedor IA, su salida debe entrar como propuesta.

El motor determinista debe ser quien decide si es válida.

---

# 21. SIN IA TAMBIÉN DEBE FUNCIONAR

Crear asistente tipo preguntas:

```text
¿Qué quieres controlar?
[ Motor ]

¿Cuándo debe encender?
[ Cuando se presione Inicio ]

¿Qué condiciones deben cumplirse?
[x] Puerta cerrada
[x] Sin alarma

¿Cuándo debe apagarse?
[x] Botón Paro
[x] Puerta abierta
[x] Falla del dispositivo
```

Resultado:

```text
Cuando Inicio esté activo
Y Puerta esté cerrada
Y No exista alarma
Entonces Motor = ENCENDIDO.

Motor se apagará si:
- se presiona Paro
- se abre Puerta
- se detecta falla
```

Esto debe cubrir el MVP incluso sin Internet/API.

---

# 22. EXPLAINER

Cada regla debe producir explicación humana.

Ejemplo:

```text
MOTOR PRINCIPAL

Encenderá cuando:
✓ Inicio esté presionado
✓ Puerta esté cerrada
✓ No exista alarma

Se apagará cuando:
✓ se presione Paro
✓ se abra Puerta
✓ aparezca alarma

Estado ante pérdida de comunicación:
APAGADO
```

---

# 23. VALIDATION ENGINE

Ejecutar múltiples validadores:

```text
IValidationRule
```

Validaciones mínimas:

## Modelo

- IDs duplicados
- nombres inválidos
- referencias inexistentes
- variable deshabilitada
- driver inexistente
- dirección vacía
- binding duplicado

## Tipos

- bool vs numeric
- strings donde no corresponden
- conversiones peligrosas
- escala inválida
- min > max

## Lógica

- reglas imposibles
- condición siempre true
- condición siempre false cuando sea detectable
- salidas sin failsafe
- ciclos combinacionales
- múltiples escritores
- temporizadores negativos
- timers sin reset cuando corresponda
- contadores inválidos

## Hardware

- dispositivo offline
- dirección fuera de rango
- slave id inválido
- puerto inválido
- velocidad serial inválida

## Operación

- salida física sin aprobación
- edición durante ejecución
- programa no probado
- programa con errores
- cambio de protocolo sin revalidación

---

# 24. SIMULADOR

El mismo LogicProgram debe correr en:

```text
RuntimeMode.Simulation
RuntimeMode.Physical
```

No crear dos motores distintos.

El simulador sustituye drivers por `VirtualDeviceDriver`.

## Simulación manual

Usuario cambia sensores desde UI.

## Simulación automática

Crear escenarios:

```text
At 0s DoorClosed = true
At 1s Start = true
At 5s DoorClosed = false
```

Registrar timeline.

---

# 25. TEST GENERATOR

Cada regla puede derivar casos.

Ejemplo:

```text
Motor ON when:
Start=true
Door=true
Alarm=false
```

Generar:

```text
Start=true, Door=true, Alarm=false => ON
Start=false, Door=true, Alarm=false => OFF
Start=true, Door=false, Alarm=false => OFF
Start=true, Door=true, Alarm=true => OFF
```

Para AND/OR/NOT generar combinaciones razonables evitando explosión combinatoria.

---

# 26. SHADOW MODE

Modo obligatorio antes de activar hardware nuevo.

```text
PHYSICAL INPUTS
      ↓
   Logic
      ↓
Proposed Outputs
      X
NO WRITE
```

Mostrar:

```text
"Atlas habría encendido Motor1"
```

El usuario puede observar antes de permitir escritura.

---

# 27. MODO OPERATIVO

Estados del proyecto:

```text
Draft
Validated
SimulationReady
SimulationPassed
ShadowReady
ShadowPassed
ReadyForActivation
Active
Paused
Faulted
Archived
```

No saltar estados silenciosamente.

---

# 28. VERSIONADO

Toda modificación de lógica crea versión.

```text
ProgramVersion
- ProgramId
- VersionNumber
- DefinitionJson
- CreatedUtc
- CreatedBy
- Reason
- Hash
```

Permitir:

- diff,
- rollback,
- activar versión previa.

---

# 29. HASH DE CONFIGURACIÓN

Calcular SHA-256 de:

- programa lógico,
- variables,
- bindings,
- dispositivos.

Runtime debe informar exactamente qué versión ejecuta.

---

# 30. AUDITORÍA

Registrar:

```text
AuditEvent
- Id
- TimestampUtc
- User
- Action
- EntityType
- EntityId
- OldValue
- NewValue
- Result
- IpAddress
- CorrelationId
```

Eventos importantes:

- login
- conexión dispositivo
- modificación binding
- nueva lógica
- validación
- simulación
- activación
- pausa
- force
- output write
- fault
- rollback

---

# 31. MANUAL / AUTO / FORCE

Estados de variable/salida:

```text
Automatic
Manual
Forced
Disabled
```

Forzar una salida debe:

1. pedir confirmación,
2. mostrar riesgo,
3. registrar auditoría,
4. mostrar indicador persistente,
5. tener expiración opcional,
6. poder cancelarse inmediatamente.

---

# 32. ALARMAS

Modelo:

```text
AlarmDefinition
AlarmInstance
```

Severidades:

```text
Info
Warning
High
Critical
```

Estados:

```text
Inactive
ActiveUnacknowledged
ActiveAcknowledged
ClearedUnacknowledged
Closed
```

Campos:

- mensaje,
- origen,
- timestamp,
- acknowledge,
- usuario,
- duración,
- variable relacionada.

---

# 33. HISTORIAN LIGERO

No guardar todo indiscriminadamente.

Políticas:

```text
OnChange
Interval
AlarmOnly
Disabled
```

Aplicar deadband a analógicas.

Separar:

- runtime memory,
- historian persistence.

---

# 34. MODBUS

Proyecto:

```text
AtlasSoftPlc.Protocols.Modbus
```

Implementar adapter sobre `NModbus`.

Configuraciones:

## TCP

```text
Host
Port (default 502)
UnitId
TimeoutMs
Retries
```

## RTU

```text
SerialPort
BaudRate
Parity
DataBits
StopBits
UnitId
TimeoutMs
Retries
```

Tipos:

```text
Coil
DiscreteInput
HoldingRegister
InputRegister
```

Soportar:

- bool,
- 16 bits,
- 32 bits,
- float,
- endianness configurable.

No asumir orden de palabras universal.

---

# 35. OPC UA

Proyecto:

```text
AtlasSoftPlc.Protocols.OpcUa
```

Usar stack oficial OPC Foundation .NET.

Primera fase:

- OPC UA Server
- exponer variables Runtime
- lectura
- subscription
- escritura solo si variable tiene permiso

Después:

- Client
- discovery
- browsing
- import de variables

Seguridad:

- certificados X.509,
- trust list,
- no aceptar automáticamente certificados desconocidos en producción.

---

# 36. MQTT

Proyecto:

```text
AtlasSoftPlc.Protocols.Mqtt
```

Usar MQTTnet.

No meter MQTT en la lógica del scan.

Publicar mediante cola desacoplada:

```text
Runtime event
   ↓
Channel<T>
   ↓
MqttPublisherService
```

---

# 37. UNIVERSAL DEVICE ADAPTER

Contrato:

```text
IDeviceDriver
IDeviceDiscovery
ITagBrowser
IDeviceDiagnostics
```

Capacidades declaradas:

```text
CanRead
CanWrite
CanBrowse
CanDiscover
CanSubscribe
SupportsBulkRead
SupportsBulkWrite
SupportsQuality
```

El front adapta opciones según capacidades.

---

# 38. AUTO-DISCOVERY

Nunca escribir al descubrir.

Discovery = READ ONLY.

Posibles mecanismos:

- red local,
- OPC UA discovery,
- configuración Modbus conocida,
- lista manual IP,
- serial ports,
- módulos Atlas ESP32.

Al encontrar dispositivo:

```text
FoundDevice
- IP
- ProtocolCandidates
- Vendor?
- Product?
- Confidence
```

No inventar fabricante.

---

# 39. ESP32 COMO REMOTE I/O

No poner la lógica principal en ESP32 inicialmente.

Arquitectura:

```text
PC Atlas SoftPLC
      │
      ├── Modbus TCP
      │
   ESP32 Remote I/O
      │
   GPIO / ADC / relés
```

ESP32 debe implementar:

- watchdog,
- safe state,
- heartbeat,
- mapping de GPIO,
- lectura digital,
- lectura analógica,
- escritura digital.

Si pierde heartbeat del PC:

```text
outputs -> configured safe state
```

---

# 40. FRONTEND — PANTALLAS

## 40.1 Dashboard

Mostrar:

- runtime Running/Stopped/Faulted
- modo Simulation/Shadow/Physical
- scan actual
- overruns
- dispositivos online/offline
- entradas
- salidas
- alarmas activas
- versión ejecutándose

## 40.2 Proyecto

- nombre
- descripción
- runtime mode
- versión
- estado
- activación

## 40.3 Dispositivos

Cards:

```text
Motor Controller
192.168.1.40
Modbus TCP
ONLINE
24 variables
```

Acciones:

- detectar
- agregar
- probar conexión
- editar
- desconectar

## 40.4 Variables

Tabla:

```text
Nombre
Tipo
I/O
Valor
Unidad
Calidad
Origen
Modo
```

## 40.5 Automatizaciones

No abrir con Ladder.

Mostrar frases/reglas.

```text
[ Cuando ] Inicio
[ Y ] Puerta cerrada
[ Entonces ] Motor → Encender
```

## 40.6 Crear automatización

Dos pestañas:

```text
DESCRÍBELO
ASISTENTE
```

## 40.7 Simulación

Panel:

```text
INPUTS        PROCESS       OUTPUTS
```

Timeline debajo.

## 40.8 Diagnóstico

- scan timing
- connections
- last communication
- retries
- errors
- stale tags

## 40.9 Alarmas

## 40.10 Historial

## 40.11 Auditoría

## 40.12 Configuración

---

# 41. SIGNALR

Usar SignalR para:

- valores live,
- estado runtime,
- alarmas,
- cambios de calidad,
- scan metrics.

NO hacer polling cada 50 ms desde navegador.

Browser no participa del control.

Si el navegador se cierra, el PLC continúa ejecutándose.

---

# 42. MVC CONTROLLERS

Crear como mínimo:

```text
HomeController
ProjectsController
RuntimeController
DevicesController
VariablesController
BindingsController
AutomationsController
SimulationController
AlarmsController
HistorianController
DiagnosticsController
AuditController
SettingsController
ApiController o endpoints específicos cuando tenga sentido
```

Controllers delgados.

No poner lógica industrial en controllers.

---

# 43. APPLICATION SERVICES

```text
ProjectService
DeviceService
VariableService
BindingService
AutomationService
LogicCompilerService
ValidationService
SimulationService
ActivationService
AlarmService
HistorianService
DiagnosticsService
AuditService
ImportExportService
```

---

# 44. RUNTIME SERVICES

```text
PlcRuntimeService
ScanCoordinator
InputImageBuilder
LogicExecutor
TimerManager
CounterManager
OutputArbiter
OutputWriter
RuntimeStateStore
WatchdogService
ScanMetricsCollector
```

---

# 45. INFRASTRUCTURE

Implementar:

```text
Repositories
SQLite persistence
JSON serialization
File export/import
Audit persistence
Historian persistence
```

Persistencia fuera del ciclo crítico.

---

# 46. CONCURRENCIA

El scan debe ser single-writer.

Regla:

```text
Runtime state mutated only by runtime loop
```

UI/config envía comandos mediante:

```text
Channel<RuntimeCommand>
```

Ejemplos:

```text
ActivateProgramCommand
PauseCommand
SetManualInputCommand
ForceOutputCommand
ClearForceCommand
```

---

# 47. CONFIGURACIÓN DURANTE EJECUCIÓN

No modificar el programa activo in-place.

Flujo:

```text
Active Version 5
      │
User edits
      ▼
Draft Version 6
      │
Validate
      │
Simulate
      │
Activate
      ▼
Atomic swap
Version 6 active
```

Si falla activación, conservar V5.

---

# 48. ATOMIC PROGRAM SWAP

Construir nueva instancia lógica completamente validada.

Después:

```text
Interlocked.Exchange(...)
```

o mecanismo seguro equivalente.

Nunca dejar runtime con media configuración.

---

# 49. STARTUP

Secuencia:

```text
Load configuration
↓
Validate database
↓
Load last known runtime configuration
↓
Validate hash
↓
Initialize drivers
↓
DO NOT WRITE OUTPUTS
↓
Read initial inputs
↓
Build runtime
↓
Enter safe/stopped state
↓
Require configured auto-start policy
```

Default:

```text
AutoStartPhysical = false
```

---

# 50. SHUTDOWN

Secuencia:

```text
Stop accepting activation commands
↓
Set runtime Stopping
↓
Attempt failsafe outputs
↓
Stop drivers
↓
Flush persistence
↓
Record shutdown
```

---

# 51. EXCEPTION POLICY

Nunca:

```csharp
catch { }
```

Cada excepción debe:

- clasificarse,
- tener correlation id,
- registrar contexto,
- no matar arbitrariamente el proceso,
- colocar driver/runtime en estado coherente.

---

# 52. DRIVER CIRCUIT BREAKER

Si un dispositivo falla repetidamente:

```text
Connected
Degraded
Disconnected
RetryWaiting
```

Backoff configurable.

El scan NO debe congelarse esperando un dispositivo eternamente.

---

# 53. TIMEOUTS

Toda I/O externa requiere timeout.

Ejemplo inicial:

```text
ConnectTimeout = 3000 ms
ReadTimeout = 1000 ms
WriteTimeout = 1000 ms
```

Configurables.

---

# 54. DEVICE POLLING

No todos los tags necesitan scan de 50 ms.

Definir clases:

```text
CriticalFast
Fast
Normal
Slow
OnDemand
```

El ScanCoordinator debe leer datos de forma razonable sin crear cientos de conexiones.

---

# 55. BULK I/O

Drivers deben agrupar lecturas/escrituras cuando protocolo lo permita.

Evitar:

```text
100 variables = 100 conexiones
```

---

# 56. CALIDAD Y STALE DATA

Cada valor tiene timestamp.

Si excede tolerancia:

```text
Quality = Stale
```

Una regla puede declarar:

```text
RequireGoodQuality = true
```

Default para entradas físicas que gobiernan outputs:

```text
true
```

---

# 57. INTERLOCKS

Objeto explícito:

```text
Interlock
- Name
- Condition
- AffectedOutputs
- SafeValue
- Priority
```

Ejemplo:

```text
DoorOpenInterlock
Condition = DoorClosed == false
Affected = Motor
SafeValue = false
```

No esconder interlocks dentro de código arbitrario.

---

# 58. ESTADOS / SECUENCIAS

Además de reglas simples implementar StateMachine en fase 2.

Ejemplo:

```text
Idle
Starting
Running
Stopping
Fault
```

Transiciones explícitas.

Esto permite representar máquinas sin exigir SFC al operador.

---

# 59. IMPORT / EXPORT

Formato Atlas:

```text
.atlasplc.json
```

Contiene:

- metadata
- variables
- devices
- bindings
- programs
- interlocks
- alarms
- tests

Versionar schema.

Preparar futura compatibilidad con PLCopen XML.

NO prometer importación universal perfecta de proyectos Siemens/Rockwell.

---

# 60. BASE DE DATOS

Tablas mínimas:

```text
Projects
Devices
Variables
TagBindings
LogicPrograms
LogicProgramVersions
LogicRules
Interlocks
AlarmDefinitions
AlarmInstances
RuntimeEvents
AuditEvents
HistorianSamples
Settings
TestScenarios
TestRuns
```

SQLite para MVP.

---

# 61. SEGURIDAD WEB

Implementar:

- autenticación local,
- roles,
- antiforgery donde corresponda,
- HTTPS,
- validación server-side,
- encoding,
- límites de payload,
- no secretos en repositorio.

Roles:

```text
Viewer
Operator
Engineer
Administrator
```

Aunque el operador no conozca PLC, permisos siguen siendo necesarios.

---

# 62. PERMISOS

Viewer:

- ver.

Operator:

- operar,
- acknowledge alarmas,
- simulación autorizada.

Engineer:

- configurar,
- crear reglas,
- bindings,
- validar.

Administrator:

- usuarios,
- seguridad,
- drivers,
- activación física crítica.

Configurable para uso doméstico single-user.

---

# 63. CSRF / AUTH

Todas operaciones mutantes desde web:

- POST/PUT/DELETE apropiado,
- antiforgery si MVC form/fetch lo requiere,
- authorization policies.

Nunca usar GET para:

```text
TurnMotorOn
ActivateProgram
ForceOutput
```

---

# 64. RED OT

Documentar recomendación:

```text
Internet
   │
Firewall
   │
IT Network
   │
Firewall/segmentation
   │
OT Network
   │
Atlas SoftPLC + devices
```

No exponer Modbus TCP directamente a Internet.

---

# 65. IA — REGLAS DURAS

El proveedor de IA:

- NO escribe directamente outputs,
- NO conoce credenciales de devices si no es necesario,
- NO ejecuta código arbitrario,
- NO activa programas,
- NO desactiva interlocks.

La IA devuelve únicamente:

```text
AutomationIntentProposal
```

Luego Atlas valida.

Si no hay IA:

```text
todo lo demás funciona
```

---

# 66. OPCIÓN IA LOCAL/FUTURA

Crear:

```text
IAutomationIntentProvider
```

Implementaciones posibles:

```text
RuleWizardIntentProvider
LocalLlmIntentProvider
RemoteLlmIntentProvider
```

No acoplar a OpenAI/Gemini/DeepSeek.

---

# 67. OBSERVABILIDAD

Usar `ILogger`.

Métricas internas:

- scan duration,
- missed scans,
- driver latency,
- reconnect count,
- writes,
- failed writes,
- stale variables,
- active alarms.

UI diagnóstica.

---

# 68. PRUEBAS UNITARIAS OBLIGATORIAS

Domain:

- PlcValue conversions
- limits
- variables
- conditions
- arithmetic
- AND/OR/NOT
- comparisons

Runtime:

- deterministic scan
- snapshot immutability
- timers
- counters
- rising/falling edge
- output conflicts
- priority
- failsafe
- stale input
- program swap
- watchdog

---

# 69. PRUEBAS DE PROPERTY / FUZZ

Si se implementan sin dependencia pesada:

Generar combinaciones de expresiones y verificar:

- nunca crash,
- nunca divide silenciosamente por cero,
- nunca devuelve tipo imposible.

---

# 70. PRUEBAS MODBUS

Levantar server local simulado.

Pruebas:

```text
coil read/write
register read/write
timeout
disconnect
reconnect
invalid address
wrong unit
endianness
float mapping
bulk read
```

NO necesitar PLC físico para CI.

---

# 71. PRUEBAS OPC UA

Levantar server de referencia/local.

Probar:

- connect
- browse
- read
- subscription
- write autorizado
- write denegado
- certificate rejection

---

# 72. PRUEBAS E2E

Escenario 1 — Motor básico:

```text
Start + DoorClosed + !Alarm => Motor
```

Escenario 2 — Timer:

```text
Sensor => TON 5s => Fan
```

Escenario 3 — Tank:

```text
LowLevel => FillValve ON
HighLevel => FillValve OFF
```

Escenario 4 — Conflict:

```text
dos reglas escriben salida
```

Debe detectar/resolver por política.

Escenario 5 — Device loss:

```text
Motor ON
device/input stale
=> failsafe
```

Escenario 6 — Version rollback.

Escenario 7 — Shadow Mode.

Escenario 8 — Retentive counter.

---

# 73. PRUEBA "OPERADOR QUE NO SABE PLC"

Criterio central del producto.

Dar al tester únicamente:

```text
Quiero que una bomba llene un tanque cuando esté bajo y se apague cuando llegue arriba.
```

El flujo deberá permitir crear automatización sin introducir:

- coil,
- register,
- rung,
- ST,
- dirección,
- bit.

Solo debe pedir:

```text
¿Cuál sensor indica nivel bajo?
¿Cuál sensor indica nivel alto?
¿Cuál salida controla la bomba?
```

---

# 74. UX DE ERRORES

PROHIBIDO mostrar solo:

```text
ArgumentException
NullReferenceException
ModbusException 0x02
```

Mostrar:

```text
No puedo leer "Nivel del tanque".

El dispositivo "ESP32 Tanque" está conectado, pero la dirección configurada no existe.

[Revisar configuración]
[Probar conexión]
```

Detalles técnicos expandibles.

---

# 75. SETUP / FIRST RUN

Wizard:

```text
Bienvenido a Atlas SoftPLC

1 ¿Quieres simular o conectar hardware?
2 ¿Qué quieres controlar?
3 Agrega tus entradas
4 Agrega tus salidas
5 Describe la automatización
6 Simular
7 Ver resultado
8 Activar cuando estés listo
```

Default = Simulation.

---

# 76. EJEMPLO DE DEMO INICIAL

Crear plantilla:

```text
Tanque de agua
```

Variables:

```text
LowLevelSensor
HighLevelSensor
Pump
EmergencyStop
```

Reglas:

```text
IF EmergencyStop THEN Pump OFF

IF LowLevelSensor
AND NOT HighLevelSensor
AND NOT EmergencyStop
THEN Pump ON

IF HighLevelSensor
THEN Pump OFF
```

Demo totalmente virtual.

---

# 77. PLANTILLAS

Incluir:

- motor start/stop,
- tanque,
- ventilación,
- iluminación,
- temperatura,
- bomba alternada (fase 2),
- semáforo,
- conveyor simple.

Plantillas son LogicPrograms normales.

---

# 78. EDITOR AVANZADO FUTURO

Después del MVP:

- vista Ladder generada,
- Structured Text generado,
- FBD,
- SFC.

**No bloquear MVP construyendo un editor gráfico IEC completo.**

El producto principal sigue siendo Zero-Knowledge.

---

# 79. LADDER GENERADO

La UI futura puede mostrar:

```text
|----[ Start ]----[ DoorClosed ]----[/ Alarm ]----( Motor )----|
```

Pero será una **representación** del IR, no la fuente de verdad.

Fuente de verdad = Logic IR.

---

# 80. DETERMINISMO

Misma:

```text
InputSnapshot
+ ProgramVersion
+ RuntimeState inicial
```

debe producir mismo resultado lógico.

No usar IA durante el scan.

No usar DateTime aleatorio dentro de reglas.

---

# 81. PERFORMANCE

MVP meta razonable:

- 1,000 variables virtuales
- 250 reglas sencillas
- scan 50 ms en PC común
- sin UI abierta también debe funcionar

Crear benchmark simple.

No optimizar prematuramente sacrificando correctitud.

---

# 82. ASIGNACIÓN DE MEMORIA

Evitar allocations masivas por scan.

No serializar JSON durante cada scan.

No escribir DB durante cada scan.

No generar strings de logs por cada variable sin necesidad.

---

# 83. SIGNALR RATE LIMITING

Runtime puede scanear 20 veces/s.

UI no necesita 20 actualizaciones completas/s.

Agrupar cambios.

Ejemplo:

```text
UI refresh 5–10 Hz
```

solo variables cambiadas.

---

# 84. DRIVER MOCKS

Cada driver real requiere mock/fake.

```text
FakeDeviceDriver
FaultingDeviceDriver
SlowDeviceDriver
FlappingDeviceDriver
```

---

# 85. CHAOS TESTS

Probar:

- conexión cae durante write,
- read tarda más del scan,
- driver lanza excepción,
- database temporalmente bloqueada,
- navegador desaparece,
- programa cambia mientras runtime opera,
- reinicio inesperado.

---

# 86. CRASH RECOVERY

Al iniciar después de crash:

```text
DetectedUncleanShutdown = true
```

Entrar detenido/safe.

No asumir que outputs anteriores siguen correctos.

Mostrar evento al operador.

---

# 87. AUDITABILIDAD DE INTENCIÓN

Guardar:

```text
SourceIntent
GeneratedRule
ValidationReport
SimulationReport
ApprovedBy
ApprovedUtc
```

Se debe poder responder:

```text
¿Por qué está encendido Motor1?
```

Con:

```text
Motor1 está encendido por Regla "Arranque principal".
Start=true
DoorClosed=true
Alarm=false
Program Version=7
```

---

# 88. TRACE DE REGLAS

Modo diagnóstico opcional:

```text
Rule 12:
Start = TRUE
DoorClosed = TRUE
Alarm = FALSE
Condition = TRUE
Action Motor = TRUE
```

No registrar cada scan permanentemente por defecto.

---

# 89. DIGITAL TWIN SIMPLE

Cada elemento visual puede tener estado:

```text
Motor:
Stopped
Running
Faulted
Unknown
```

Tanque:

```text
Level 0..100
```

Solo visualización; no crear motor 3D.

---

# 90. IMPORTANCIA DE OPC UA

OPC UA será la salida universal principal porque abstrae:

- estructura,
- semántica,
- tipos,
- seguridad,
- datos,
- command/control.

Atlas puede exponer:

```text
Objects/
  Atlas/
    Project/
      Inputs/
      Outputs/
      Memory/
      Alarms/
      Diagnostics/
```

---

# 91. DRIVER ROADMAP

## Fase 1

```text
Virtual
Modbus TCP
Modbus RTU
OPC UA Server
```

## Fase 2

```text
OPC UA Client
MQTT
ESP32 Remote I/O
```

## Fase 3

```text
Siemens S7 via S7.Net+
Rockwell via libplctag.NET
```

No implementar todo simultáneamente antes de tener runtime correcto.

---

# 92. DEFINITION OF DONE — MVP

MVP NO termina hasta que:

- compila Release,
- 0 errores,
- idealmente 0 warnings propios,
- pruebas pasan,
- runtime ejecuta scan real,
- simulador funciona,
- usuario crea regla sin PLC,
- timers funcionan,
- counters funcionan,
- interlocks funcionan,
- conflicts se detectan,
- failsafe funciona,
- versiones funcionan,
- Modbus TCP funciona contra simulador,
- UI recibe valores en vivo,
- Shadow mode existe,
- ningún Controller contiene motor lógico,
- hardware write requiere activación.

---

# 93. ORDEN DE IMPLEMENTACIÓN PARA EL AGENTE

Ejecutar exactamente en este orden salvo bloqueo técnico real.

## BLOQUE A — Foundation

1. solución
2. proyectos
3. Domain
4. tests Domain
5. persistence mínima

## BLOQUE B — Runtime

6. variable store
7. snapshots
8. expressions
9. rules
10. timers
11. counters
12. scan engine
13. output arbiter
14. failsafe
15. tests Runtime

## BLOQUE C — Virtual

16. VirtualDevice
17. simulator
18. scenario runner
19. simulation UI

## BLOQUE D — Web

20. dashboard
21. projects
22. variables
23. devices
24. automations
25. wizard
26. diagnostics
27. SignalR

## BLOQUE E — Physical Protocol

28. NModbus
29. TCP client
30. local test server
31. bindings
32. communication diagnostics

## BLOQUE F — Universal

33. OPC UA Server
34. expose tags
35. security baseline

## BLOQUE G — Hardening

36. versioning
37. audit
38. alarms
39. historian
40. shadow mode
41. crash recovery
42. authorization

## BLOQUE H — Finish

43. integration tests
44. E2E tests
45. README
46. architecture documentation
47. operator manual
48. release build

---

# 94. REGLA PARA EL AGENTE: NO DEJAR TODO EN TODO

No crear:

```text
TodoService
RuntimeManagerWithEverything
Helpers.cs
Utils.cs
Common.cs
```

Usar responsabilidades claras.

---

# 95. REGLA PARA EL AGENTE: ARCHIVOS COMPLETOS

Cuando modifique funcionalidad:

- terminar implementación,
- agregar tests,
- ejecutar tests,
- corregir,
- volver a ejecutar.

No dejar pseudocódigo.

No dejar TODO críticos.

No fingir integración.

---

# 96. REGLA PARA EL AGENTE: INVESTIGAR ANTES DE INVENTAR

Para cada protocolo:

1. leer documentación oficial,
2. confirmar librería,
3. confirmar licencia,
4. construir adapter,
5. pruebas contra simulador/referencia.

No implementar protocolo industrial “de memoria”.

---

# 97. REGLA DE TOKENS / AUTONOMÍA PARA DEEPSEEK

Priorizar código terminado sobre explicaciones largas.

Ciclo:

```text
READ
→ PLAN SHORT
→ IMPLEMENT
→ BUILD
→ TEST
→ FIX
→ VERIFY
→ NEXT
```

No volver a describir toda la arquitectura después de cada cambio.

No detenerse por errores corregibles.

Solo detenerse cuando:

- falta una credencial/hardware indispensable,
- hay una decisión destructiva imposible de inferir,
- dependencia legal/licencia impide continuar.

En esos casos continuar con módulos no bloqueados.

---

# 98. CRITERIO DE CALIDAD

Preferir:

```text
simple
deterministic
testable
replaceable
observable
```

sobre:

```text
clever
massive abstraction
reflection magic
dynamic runtime code
```

---

# 99. README OBLIGATORIO

Debe explicar:

```text
¿Qué es Atlas SoftPLC?
¿Cómo instalar?
¿Cómo ejecutar?
¿Cómo crear una simulación?
¿Cómo agregar un dispositivo Modbus?
¿Cómo crear una automatización?
¿Cómo pasar a Shadow?
¿Cómo activar Physical?
¿Cómo volver a modo seguro?
```

---

# 100. DOCUMENTACIÓN DE ARQUITECTURA

Crear:

```text
docs/
  ARCHITECTURE.md
  RUNTIME.md
  LOGIC_IR.md
  DEVICE_DRIVERS.md
  MODBUS.md
  OPCUA.md
  SECURITY.md
  TESTING.md
  OPERATOR_GUIDE.md
  DEVELOPMENT.md
```

---

# 101. FUENTES TÉCNICAS A CONSULTAR

## IEC / PLCopen

- https://plcopen.org/iec-61131-3
- https://www.plcopen.org/downloads/

Consultar especialmente:

- IEC 61131-3 overview
- Structured Text
- Ladder Diagram
- Sequential Function Chart
- PLCopen XML
- PLCopen OPC UA Information Model for IEC 61131-3
- Coding Guidelines
- Software Quality Metrics

## OPC Foundation

- https://reference.opcfoundation.org/
- https://github.com/OPCFoundation/UA-.NETStandard

Consultar:

- OPC UA Part 1 Overview and Concepts
- Part 2 Security Model
- Application authentication
- X.509 certificates
- TrustLists
- subscriptions
- information model
- client/server architecture

## Modbus

- https://www.modbus.org/modbus-specifications
- Modbus Application Protocol Specification V1.1b3
- Modbus TCP/IP
- Modbus Serial Line Protocol and Implementation Guide

## NModbus

- https://github.com/NModbus/NModbus

## MQTT

- https://github.com/dotnet/MQTTnet

## Sparkplug

- https://sparkplug.eclipse.org/specification/

## Rockwell / EtherNet-IP

- https://www.odva.org/technology-standards/key-technologies/ethernet-ip/
- https://www.odva.org/technology-standards/key-technologies/common-industrial-protocol-cip/
- https://github.com/libplctag/libplctag.NET

## Siemens

- https://github.com/S7NetPlus/s7netplus

## Seguridad ICS/OT

- NIST SP 800-82 Rev. 3
- IEC 62443 concepts
- OPC UA Security Model

## Microsoft .NET

Consultar documentación oficial de:

- ASP.NET Core MVC
- BackgroundService / IHostedService
- SignalR
- Channel<T>
- PeriodicTimer
- TimeProvider
- Microsoft.Data.Sqlite
- logging
- configuration
- authorization
- health checks

---

# 102. DECISIONES DE LICENCIAS

Preferidas:

```text
NModbus        MIT
S7.Net+        MIT
MQTTnet        MIT
libplctag.NET  MPL-2.0
```

Para OPC Foundation, revisar y conservar avisos/licencia aplicable de los paquetes oficiales utilizados.

Mantener archivo:

```text
THIRD_PARTY_NOTICES.md
```

con:

- package,
- version,
- repository,
- license,
- purpose.

---

# 103. PRIMER RESULTADO VISUAL ESPERADO

Al abrir:

```text
ATLAS SOFTPLC

Runtime: ● RUNNING
Mode: SIMULATION
Scan: 49.8 ms
Program: Tanque v1

ENTRADAS
Nivel bajo       ON
Nivel alto       OFF
Paro emergencia  OFF

SALIDAS
Bomba            ON

AUTOMATIZACIÓN ACTIVA
"Cuando el nivel esté bajo y el tanque no esté lleno,
encender la bomba."

¿Por qué está encendida?
[Ver explicación]
```

Eso representa mejor la misión que un editor Ladder vacío.

---

# 104. PRIMER FLUJO COMPLETO DE ACEPTACIÓN

```text
Inicio
  ↓
Crear proyecto "Tanque"
  ↓
Elegir SIMULACIÓN
  ↓
Crear sensor "Nivel bajo"
  ↓
Crear sensor "Nivel alto"
  ↓
Crear salida "Bomba"
  ↓
Escribir:
"Enciende la bomba cuando el nivel esté bajo
y apágala cuando llegue al nivel alto"
  ↓
Atlas interpreta
  ↓
Atlas muestra regla
  ↓
Atlas valida
  ↓
Atlas genera pruebas
  ↓
Atlas simula
  ↓
Usuario cambia Nivel bajo
  ↓
Bomba ON
  ↓
Usuario cambia Nivel alto
  ↓
Bomba OFF
  ↓
Timeline correcto
  ↓
PASS
```

---

# 105. CRITERIO FINAL DEL PRODUCTO

El objetivo NO es que Atlas diga:

```text
"Puedes programar un PLC."
```

El objetivo es que Atlas permita decir:

```text
"Quiero que esta máquina haga esto."
```

y que el software transforme esa intención en:

```text
modelo explícito
+ lógica determinista
+ validaciones
+ simulación
+ pruebas
+ explicación
+ operación
+ diagnóstico
+ integración industrial
```

sin requerir que el operador aprenda los detalles del PLC.

---

# 106. MANDATO FINAL PARA EL AGENTE

Construye el sistema, no una demo falsa.

Prioriza en este orden:

```text
CORRECTITUD
→ SEGURIDAD OPERACIONAL
→ DETERMINISMO
→ TESTS
→ MANTENIBILIDAD
→ UX CERO-CONOCIMIENTO
→ PROTOCOLOS
→ ESTÉTICA
```

Cuando exista una duda, elige la opción que:

- reduzca acoplamiento,
- mantenga al runtime independiente de la UI,
- mantenga protocolos detrás de interfaces,
- mantenga IA fuera del scan,
- preserve simulación,
- preserve auditabilidad,
- preserve failsafe,
- pueda probarse sin hardware.

**No detenerse después de diagnosticar un problema corregible. Corregirlo, compilar, probar y continuar.**

**Fin de la especificación maestra.**
