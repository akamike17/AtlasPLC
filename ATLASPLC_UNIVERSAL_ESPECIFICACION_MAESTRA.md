# ATLASPLC — ESPECIFICACIÓN MAESTRA QUIRÚRGICA
## Workbench universal para crear, validar, simular, traducir y desplegar proyectos PLC

**Estado:** documento rector de arquitectura e implementación.  
**Destino:** agente de código (DeepSeek/Hermes/Codex u otro).  
**Prioridad:** corregir el rumbo del producto sin tirar el trabajo útil ya realizado.  
**Regla:** NO convertir AtlasPLC en un SoftPLC monolítico ni en una colección de demos hardcodeadas.

---

# 0. MISIÓN EXACTA DEL PRODUCTO

AtlasPLC debe ser una **estación universal de ingeniería PLC asistida**, orientada a usuarios con poco o ningún conocimiento de PLC.

El usuario debe poder:

1. crear un proyecto de automatización en lenguaje natural, asistente visual o bloques;
2. describir sensores, actuadores, condiciones, secuencias, tiempos y restricciones físicas;
3. hacer que Atlas convierta eso a un modelo lógico interno determinista;
4. detectar errores lógicos antes de ejecutar;
5. detectar incompatibilidades físicas antes de ejecutar;
6. recibir un `hint` claro de cómo corregir cada problema;
7. simular el proyecto internamente;
8. ejecutar escenarios y pruebas automáticas;
9. traducir el proyecto al formato/lenguaje requerido por un destino concreto;
10. enviarlo a un simulador compatible;
11. enviarlo a un PLC físico compatible cuando el fabricante/familia permita automatizar el despliegue;
12. leer/monitorizar PLCs compatibles;
13. verificar después del despliegue que el programa esperado quedó cargado y que el PLC está en el estado previsto.

El producto NO es:

- un “tanque de agua”;
- un “sistema de riego”;
- una aplicación específica de proceso;
- un reemplazo universal de TIA Portal/Studio 5000/GX Works/Sysmac Studio;
- “Modbus para todo”;
- un PLC físico;
- un sistema Safety PLC certificado.

Las demos de tanque, riego, banda, semáforo, bombeo, etc. son **fixtures de prueba y ejemplos de proyectos**, nunca el núcleo del producto.

---

# 1. FLUJO DE USUARIO OBLIGATORIO

```text
[NUEVO PROYECTO]
      |
      v
[¿QUÉ QUIERES CONTROLAR?]
      |
      +--> Asistente guiado
      +--> Lenguaje natural
      +--> Diagrama/bloques
      +--> Importar proyecto compatible
      |
      v
[MODELO DE PLANTA]
 Sensores / actuadores / límites / energía / estados
      |
      v
[MODELO LÓGICO ATLAS / IR]
      |
      v
[VALIDACIÓN ESTÁTICA]
      |
      +--> errores de tipos
      +--> contradicciones
      +--> salidas con múltiples escritores
      +--> estados imposibles
      +--> referencias no definidas
      +--> condiciones sin salida
      +--> ciclos no válidos
      |
      v
[VALIDACIÓN FÍSICA]
      |
      +--> actuador sin interlock
      +--> bomba sin agua
      +--> válvulas incompatibles abiertas simultáneamente
      +--> motor arrancando con puerta abierta
      +--> sentido adelante/reversa simultáneo
      +--> sobrecarga ignorada
      +--> sensor requerido inexistente
      +--> salida incompatible con hardware
      |
      v
[HINTS]
 "No puede arrancar Motor1 mientras EStop está activo.
  Agrega EStop == OFF como permiso obligatorio."
      |
      v
[SIMULACIÓN DETERMINISTA ATLAS]
      |
      v
[ESCENARIOS + INVARIANTES + PRUEBAS]
      |
      +--> FALLA -> informe + hint -> volver al editor
      |
      `--> PASA
             |
             v
       [SELECCIONAR TARGET]
             |
             +--> simulador externo
             +--> PLC físico
             |
             v
       [TARGET CAPABILITY CHECK]
             |
             +--> no soportado -> explicación exacta
             |
             `--> soportado
                    |
                    v
          [GENERAR ARTEFACTO TARGET]
                    |
                    v
              [PREVIEW/DIFF]
                    |
                    v
          [CONFIRMACIÓN DE USUARIO]
                    |
                    v
         [DEPLOY / IMPORT / DOWNLOAD]
                    |
                    v
             [VERIFY ONLINE]
```

NUNCA saltar de lenguaje natural directamente a un PLC físico sin pasar por:

`IR -> validación -> simulación -> target check -> preview -> confirmación`.

---

# 2. PRINCIPIO ARQUITECTÓNICO: ATLAS IR ES LA FUENTE DE VERDAD

No usar Ladder, ST, XML de fabricante, Modbus ni ningún formato externo como modelo maestro.

Crear una **representación intermedia canónica de Atlas (Atlas IR)**.

La IR debe ser independiente de fabricante y suficientemente expresiva para modelar:

- proyecto;
- configuración;
- tareas;
- ciclo/scan;
- programas;
- funciones;
- function blocks;
- variables;
- constantes;
- tipos;
- estructuras;
- arrays;
- temporizadores;
- contadores;
- comparadores;
- operaciones booleanas;
- operaciones numéricas;
- estados;
- secuencias;
- transiciones;
- interlocks;
- alarmas;
- permisos;
- latches;
- eventos;
- edges;
- prioridades;
- I/O lógico;
- I/O físico;
- restricciones físicas;
- tiempos;
- metadata;
- origen de cada elemento;
- diagnósticos;
- extensiones específicas de target.

La IR debe ser:

- serializable;
- versionada;
- validable;
- determinista;
- auditable;
- migrable;
- con IDs estables;
- independiente de UI;
- independiente de protocolo;
- independiente de runtime;
- capaz de producir múltiples backends.

## 2.1 Entidades mínimas

```text
AtlasProject
ProjectVersion
TargetSelection
ControllerProfile
TaskDefinition
ProgramUnit
FunctionDefinition
FunctionBlockDefinition
VariableDefinition
DataTypeDefinition
IoPoint
IoBinding
LogicNode
LogicEdge
StateMachine
State
Transition
TimerDefinition
CounterDefinition
InterlockRule
PhysicalConstraint
AlarmDefinition
ScenarioDefinition
AssertionDefinition
Diagnostic
SourceMap
TargetExtension
GeneratedArtifact
DeploymentRecord
```

---

# 3. MODELO DE PLANTA: LA PARTE QUE PERMITE DECIR “TU PROGRAMA ESTÁ MAL FÍSICAMENTE”

Atlas no puede detectar fallas físicas si sólo conoce booleanos.

Cada proyecto debe definir un **Plant Model**.

Ejemplo:

```text
Motor M1
  tipo: Motor
  direction: ForwardOnly
  requires:
    DoorClosed == true
    Overload == false
  mutuallyExclusiveWith:
    MotorReverse
  safeState: OFF

Pump P1
  tipo: Pump
  requiresFlowSource: Tank1
  minimumTankLevelSensor: TankLow
  safeState: OFF

Valve V1
  tipo: SolenoidValve
  normally: Closed
  safeState: Closed
  mutuallyExclusiveWith: V2
```

## 3.1 Biblioteca de componentes físicos

Crear catálogo extensible:

- push button;
- selector;
- E-Stop lógico de proceso;
- proximity sensor;
- photoelectric sensor;
- level sensor;
- pressure switch;
- analog sensor;
- motor;
- reversible motor;
- VFD command;
- pump;
- solenoid valve;
- cylinder;
- heater;
- fan;
- conveyor;
- contactor;
- alarm;
- light;
- relay;
- encoder;
- analog actuator.

Cada tipo puede declarar reglas por defecto, pero el usuario puede configurar propiedades.

## 3.2 No confundir seguridad funcional con Safety PLC

Atlas puede detectar patrones peligrosos y recomendar interlocks.

Atlas NO debe afirmar que un programa generado:

- cumple SIL;
- cumple PL;
- sustituye un safety relay;
- sustituye hardware de E-Stop;
- convierte un PLC estándar en Safety PLC.

Las funciones de seguridad certificadas dependen de hardware, arquitectura y herramientas del fabricante.

---

# 4. PIPELINE DE VALIDACIÓN

Implementar como una cadena de validadores independientes.

```text
IR
 |
 +-> SchemaValidator
 +-> TypeValidator
 +-> ReferenceValidator
 +-> LogicValidator
 +-> ControlFlowValidator
 +-> TimingValidator
 +-> PhysicalValidator
 +-> SafetyHeuristicValidator
 +-> TargetCompatibilityValidator
 +-> DeploymentReadinessValidator
```

Cada hallazgo debe producir:

```text
DiagnosticId
Severity: Info | Warning | Error | Blocker
Category
ElementId
Message
Why
Evidence
Hint
AutoFixAvailable
TargetSpecific
RuleVersion
```

Ejemplo:

```text
ATLAS-PHY-0017
Severity: Blocker
Element: MainPump

Message:
La bomba puede energizarse con TankLowLevel activo.

Why:
La salida MainPump tiene al menos un camino lógico verdadero
que no exige TankLowLevel == false.

Hint:
Agrega TankLowLevel == false al permiso de marcha
o crea un interlock de bajo nivel.

Evidence:
Path: Start -> AutoMode -> MainPump
Missing constraint: TankLowLevel == false
```

No escribir “programa incorrecto” sin explicar el camino que produjo el fallo.

---

# 5. ANÁLISIS LÓGICO MÍNIMO

Detectar como mínimo:

- variables sin declarar;
- variables declaradas pero nunca usadas;
- salidas sin escritor;
- múltiples escritores incompatibles;
- assignment conflict;
- SET/RESET conflict;
- coil duplicada;
- condiciones siempre verdaderas;
- condiciones siempre falsas;
- ramas inalcanzables;
- estados inalcanzables;
- estados sin salida;
- transiciones ambiguas;
- transición con prioridad no definida;
- ciclos combinacionales inválidos;
- autoalimentación no intencional;
- temporizador imposible;
- timeout negativo/cero inválido;
- overflow/rango;
- conversión de tipo peligrosa;
- división por cero detectable;
- comparación analógica fuera de rango;
- salida actuada desde dos tareas sin arbitraje explícito;
- dependencia de orden de evaluación no declarada;
- latch que no tiene camino de reset;
- stop que no domina start;
- interlock que puede ser bypassed;
- salida energizada en estado Fault/Stop cuando su safeState es OFF.

---

# 6. ANÁLISIS FÍSICO MÍNIMO

Implementar reglas sobre Plant Model:

- dos contactores de reversa simultáneos;
- válvulas mutuamente excluyentes simultáneas;
- bomba activa sin fuente disponible;
- bomba activa con nivel mínimo inválido;
- calentador activo sin flujo cuando se definió flujo obligatorio;
- cilindro extend/retract simultáneo;
- arranque de motor con guard/door abierto si fue declarado interlock;
- actuador sin condición de paro;
- alarm condition detectada pero ignorada;
- salida con safeState no garantizado;
- secuencia que exige un sensor que no existe;
- secuencia que espera una transición físicamente imposible;
- tiempos por debajo del mínimo configurado del dispositivo;
- rango analógico incompatible;
- unidad incompatible;
- I/O asignado a canal incompatible;
- número de I/O superior a capacidad del target;
- dirección duplicada;
- consumo/capacidad sólo si el usuario proporciona datos suficientes.

No inventar datos físicos.

Si falta información, emitir:

`NeedsUserInput`

en lugar de asumir.

---

# 7. HINT ENGINE

Los hints NO deben depender inicialmente de IA generativa.

Primera implementación:

- reglas deterministas;
- templates;
- contexto del diagnóstico;
- sugerencias específicas.

IA opcional después para explicar en lenguaje natural, NUNCA para decidir si el programa es seguro.

Ejemplos:

```text
ERROR:
M1_Forward y M1_Reverse pueden estar ON a la vez.

HINT:
Crea un interlock cruzado:
M1_Forward requiere NOT M1_Reverse
M1_Reverse requiere NOT M1_Forward
```

```text
ERROR:
La secuencia espera Sensor_End durante 10 s pero no existe una transición de fallo.

HINT:
Agrega timeout:
si Sensor_End no aparece dentro de 10 s -> Fault.
```

---

# 8. SIMULADOR INTERNO

El runtime actual de Atlas se conserva y se redefine como:

**Atlas Simulation Runtime**

No es la identidad completa del producto.

Debe ejecutar Atlas IR sin conocer Siemens/Rockwell/Modbus.

Propiedades:

- scan determinista;
- clock virtual para tests;
- single-step;
- pause;
- run;
- stop;
- fault;
- watch;
- force controlado;
- trace;
- snapshots;
- scenario runner;
- deterministic replay;
- watchdog;
- failsafe;
- generation/epoch para descartar scans tardíos;
- métricas.

El trabajo actual de watchdog, failsafe, anti-stale scan y runtime se CONSERVA.

---

# 9. SCENARIO ENGINE Y PROPIEDADES

El usuario debe poder probar:

```text
Given:
  Start = OFF
  DoorClosed = ON

At 1s:
  Start = ON

Expect within 100ms:
  Motor = ON

At 5s:
  DoorClosed = OFF

Expect immediately:
  Motor = OFF
```

Además de escenarios concretos, soportar invariantes:

```text
ALWAYS NOT (Forward AND Reverse)

ALWAYS EmergencyStop -> NOT Motor

ALWAYS Pump -> ValveOpen

ALWAYS Fault -> AllSafeOutputs
```

Primero implementar un DSL sencillo propio; no hace falta construir un theorem prover.

Después se puede explorar model checking sobre subconjuntos finitos.

---

# 10. IMPORTACIÓN Y TRADUCCIÓN

## 10.1 Formato neutral de intercambio

Usar **PLCopen XML / IEC 61131-10** como formato importante de intercambio cuando sea útil, pero NO como Atlas IR.

PLCopen XML sirve para intercambiar elementos IEC 61131-3 entre herramientas y contiene información textual/gráfica. La interoperabilidad real varía por herramienta; por eso siempre debe pasar por un importador que valide qué se pudo mapear.

Pipeline:

```text
PLCopen XML
    -> Parser
    -> Import diagnostics
    -> Atlas IR
```

y:

```text
Atlas IR
    -> PLCopen exporter
    -> Target import validation
```

## 10.2 Structured Text como backend portable

Crear un backend ST canónico para targets que acepten IEC 61131-3 ST.

NO asumir que el mismo ST compila sin cambios en todos los fabricantes.

Debe existir:

```text
IecStEmitter
VendorDialectNormalizer
TargetCompilerAdapter
```

## 10.3 Round-trip

Cuando Atlas importe una construcción que no entiende completamente:

- no borrarla;
- representarla como `OpaqueTargetNode`;
- conservar source text/XML cuando sea posible;
- marcar el proyecto como `PartialRoundTrip`;
- impedir una reexportación destructiva sin advertencia.

---

# 11. TARGET ADAPTER SDK

Crear una interfaz estable.

Conceptualmente:

```csharp
public interface IPlcTargetAdapter
{
    TargetIdentity Identity { get; }
    TargetCapabilities Capabilities { get; }

    Task<ProbeResult> ProbeAsync(...);
    Task<ImportResult> ImportAsync(...);
    Task<CompatibilityReport> ValidateAsync(...);
    Task<GenerateResult> GenerateAsync(...);
    Task<BuildResult> BuildAsync(...);
    Task<DeployResult> DeployAsync(...);
    Task<VerifyResult> VerifyAsync(...);
    Task<OnlineSnapshot> ReadOnlineAsync(...);
}
```

NO exigir que todos los adapters implementen todo.

Cada capability debe declararse explícitamente.

```text
Discover
ReadLiveData
WriteLiveData
ReadSymbols
UploadSource
ImportProject
ExportProject
GenerateSource
GenerateProject
Compile
Simulate
DeployProgram
DeployHardware
StartController
StopController
VerifyDeployment
OnlineDiagnostics
Force
Rollback
```

Si una función no está soportada:

`UnsupportedCapability`

No simular éxito.

---

# 12. NIVELES DE SOPORTE POR TARGET

Atlas debe mostrar al usuario algo como:

```text
Siemens S7-1500 / TIA V21
Discovery             YES
Read live values       YES
Generate program       YES
Create/update project  YES (TIA Openness requerido)
Compile                YES (TIA instalado)
Download to PLC        YES (TIA Openness + condiciones del target)
Upload source          DEPENDE
Hardware config        PARCIAL/YES según objeto
```

No usar una etiqueta vaga “Compatible”.

Definir niveles:

```text
L0  Unsupported
L1  Monitor
L2  Import/Export source
L3  Generate compatible project/artifact
L4  Build/Compile through vendor tool
L5  Direct/automated deployment
L6  Online verify/diagnostics
```

Un modelo puede tener diferente nivel que otro de la misma marca.

---

# 13. TARGET PROFILE DATABASE

Crear una base versionada y actualizable:

```text
Manufacturer
Family
Model
OrderNumber
FirmwareRange
EngineeringTool
EngineeringToolVersion
Languages
ProtocolCapabilities
ImportFormats
ExportFormats
DeployMethod
SimulatorTargets
IoLimits
DataTypeLimits
InstructionLimits
TaskLimits
KnownQuirks
SafetyFamily
CapabilityLevel
DocumentationLinks
ProfileVersion
```

El usuario debe seleccionar:

`Marca -> Familia -> Modelo -> Firmware`

o permitir que Atlas detecte cuando exista mecanismo fiable.

NUNCA generar un proyecto “Siemens genérico” sin conocer el target.

---

# 14. ESTRATEGIA REAL POR ECOSISTEMA

## 14.1 Siemens

Camino preferido:

```text
Atlas IR
 -> Siemens backend
 -> SCL/LAD/SimaticML según capacidad
 -> TIA Portal project
 -> TIA Portal Openness
 -> Compile
 -> DownloadProvider
 -> PLC / PLCSIM compatible
 -> Verify
```

TIA Portal Openness permite automatizar ingeniería y expone operaciones de creación/lectura/modificación/importación/exportación. También ofrece `DownloadProvider` para descargar software/hardware a targets descargables.

Por tanto Siemens es viable como target serio, PERO requiere una versión compatible de TIA Portal/Openness instalada y permisos/configuración apropiados.

No intentar reimplementar el descargador Siemens desde cero.

## 14.2 Rockwell / Allen-Bradley Logix

Camino inicial:

```text
Atlas IR
 -> Rockwell backend
 -> L5X/L5K
 -> Studio 5000 import
 -> compile/download mediante mecanismo soportado
 -> verify
```

Studio 5000 soporta import/export de proyectos/componentes mediante L5K/L5X y L5X puede contener programas, rutinas, rungs Ladder, tags, UDTs y otros elementos.

Primera meta NO es escribir ACD binario.

Generar L5X/L5K válidos y verificables.

Después integrar automatización oficial disponible por versión/licencia.

## 14.3 Beckhoff TwinCAT

Target prioritario porque TwinCAT expone **Automation Interface** vía COM utilizable desde C#/.NET.

Camino:

```text
Atlas IR
 -> IEC ST / TwinCAT project generator
 -> TwinCAT Automation Interface
 -> create/open project
 -> configure
 -> build
 -> activate/download cuando sea soportado
 -> verify
```

## 14.4 Schneider Machine Expert / ecosistema CODESYS

Schneider Machine Expert soporta import/export PLCopenXML y expone scripting para importar/exportar PLCopenXML.

Camino:

```text
Atlas IR
 -> PLCopen XML / ST
 -> Machine Expert scripting
 -> project
 -> compile
 -> deploy usando herramienta del fabricante
```

Es un excelente target temprano para demostrar portabilidad basada en estándar + automatización.

## 14.5 CODESYS

Investigar/usar Automation Interface/scripting oficial instalado con el entorno cuando corresponda.

No acoplar Atlas al formato binario interno del proyecto.

Preferir:

`Atlas IR -> PLCopen XML/ST -> automation API -> build/deploy`

según capacidades y licencia/instalación.

## 14.6 Omron Sysmac

Tratar `.smc/.smc2` como formato de ecosistema propietario.

No inventar generador binario.

Primera fase:

- target profile;
- ST/IEC mapping;
- import/export documentado cuando sea oficialmente soportado;
- integración con herramienta oficial sólo mediante API/documentación disponible.

## 14.7 Mitsubishi MELSOFT

Mismo principio:

- NO escribir binarios de proyecto sin especificación;
- usar formatos/texto/importación oficiales;
- automatizar GX Works sólo cuando exista interfaz oficialmente soportada para la versión objetivo;
- capability matrix exacta por familia.

## 14.8 Generic Modbus

Modbus NO programa PLCs.

El adapter Modbus actual se conserva para:

- I/O simulation;
- integration testing;
- online monitor;
- commissioning;
- remote I/O;
- dispositivos genéricos.

Debe renombrarse conceptualmente a:

`Protocol/OnlineData Adapter`

NO `UniversalDeploymentAdapter`.

---

# 15. SIMULADORES EXTERNOS

Atlas debe soportar simuladores a través de adapters.

No existe “enviar a cualquier simulador” sin conocer su interfaz.

Crear:

```text
IExternalSimulatorAdapter
```

Capabilities:

```text
ImportArtifact
Launch
Deploy
Start
Stop
SetInput
ReadOutput
ReadTrace
Reset
FaultInject
```

Targets iniciales de laboratorio:

1. Atlas Internal Simulation Runtime.
2. OpenPLC como runtime/PLC software abierto donde el flujo de integración disponible sea estable.
3. CODESYS/TwinCAT/PLCSIM cuando estén disponibles en el equipo.
4. ModRSsim2 únicamente como simulador de dispositivo Modbus para pruebas de protocolo, NO como simulador completo de proyecto PLC.
5. 4diac sólo como adapter separado si se decide soportar IEC 61499; no mezclar IEC 61499 con IEC 61131-3 silenciosamente.

---

# 16. “LECTOR UNIVERSAL” — DEFINIR QUÉ SIGNIFICA LEER

“Leer PLC” tiene varios significados.

Atlas debe declarar por target:

```text
CanDiscoverDevice
CanReadIdentity
CanReadFirmware
CanReadIoState
CanReadTags
CanReadSymbolTable
CanReadDiagnostics
CanUploadBinary
CanUploadProgramSource
CanRecoverComments
CanRecoverHardwareConfig
CanRecoverFullProject
```

MUY IMPORTANTE:

Leer valores online NO implica poder recuperar el proyecto fuente.

Muchos PLCs pueden contener:

- protección de know-how;
- contraseña;
- source no descargado;
- símbolos ausentes;
- comentarios sólo en proyecto de ingeniería;
- bloques compilados sin fuente recuperable.

Atlas debe decir:

```text
"Se puede monitorizar este PLC, pero no reconstruir de forma fiel
el proyecto fuente desde el dispositivo."
```

Nunca inventar código faltante.

---

# 17. DESCUBRIMIENTO

Separar descubrimiento de programación.

```text
DeviceDiscoveryService
```

Fuentes/adapters:

- redes/protocolos oficialmente soportados;
- OPC UA discovery;
- Modbus known endpoint scan sólo en rangos explícitamente autorizados;
- vendor discovery APIs;
- import de archivo de proyecto;
- entrada manual.

No realizar escaneo agresivo de redes industriales por defecto.

---

# 18. COMPILACIÓN

Atlas NO necesita escribir un compilador binario para cada CPU desde cero.

Pipeline:

```text
Atlas IR
   |
   +-> semantic lowering
   |
   +-> target compatibility
   |
   +-> target source/project generator
   |
   +-> vendor compiler/toolchain adapter
   |
   +-> compiler diagnostics importer
   |
   +-> Atlas diagnostics
```

Si el fabricante obliga a usar su toolchain:

Atlas lo orquesta.

Eso sigue siendo una experiencia unificada para el usuario.

---

# 19. DEPLOYMENT TRANSACTION

Todo deploy debe tratarse como transacción auditable.

```text
1. Identify target
2. Read identity/firmware
3. Compare against selected profile
4. Check authorization
5. Snapshot/backup when capability exists
6. Generate artifact
7. Build/compile
8. Show diagnostics
9. Show diff/summary
10. Require explicit confirmation
11. Put target in required state if necessary
12. Deploy
13. Verify hash/version/signature where possible
14. Read diagnostics
15. Start/return to requested state
16. Record deployment
```

Nunca:

`click Deploy -> enviar a IP sin verificar modelo`.

---

# 20. PROJECT PACKAGE

Un proyecto Atlas debe ser portable como:

```text
.atlasplc
```

Preferible ZIP versionado con archivos legibles:

```text
/project.json
/ir.json
/plant.json
/scenarios/
/targets/
/artifacts/
/docs/
/imports/
/traces/
manifest.json
```

No guardar secretos dentro.

Manifest:

```text
SchemaVersion
ProjectId
ProjectVersion
CreatedAt
ModifiedAt
IrHash
TargetProfiles
RequiredFeatures
```

---

# 21. UX PARA USUARIO SIN CONOCIMIENTO PLC

El usuario NO debe empezar viendo direcciones PLC.

Flujo:

```text
¿Qué deseas controlar?

[ Motor ]
[ Bomba ]
[ Banda ]
[ Válvula ]
[ Semáforo ]
[ Proceso personalizado ]
```

Después:

```text
¿Qué hace que arranque?
¿Qué lo detiene?
¿Qué situaciones deben impedir que arranque?
¿Qué sensores confirman movimiento/posición?
¿Qué debe ocurrir si el sensor no responde?
```

Atlas produce la lógica.

Modo avanzado:

- Ladder;
- ST;
- FBD;
- SFC;
- I/O mapping;
- target details.

Modo básico:

- lenguaje natural;
- bloques;
- asistente;
- simulación visual;
- errores/hints.

Ambos modifican la misma IR.

---

# 22. EDICIÓN GRÁFICA

No construir cuatro editores completos de IEC en la primera fase.

Orden:

1. editor de bloques/reglas Atlas;
2. secuencias/estado;
3. vista Ladder generada/limitada;
4. ST generado y editable con source mapping;
5. FBD/SFC posteriormente.

Siempre conservar:

`UI node <-> IR node <-> generated target source`

mediante SourceMap.

---

# 23. QUÉ SE CONSERVA DEL TRABAJO ACTUAL

NO tirar lo que ya funciona.

## Conservar

- Domain/Application/Infrastructure/Runtime por capas;
- scan coordinator;
- runtime state machine;
- watchdog;
- failsafe;
- force metadata;
- audit;
- anti-stale scan generation;
- Modbus TCP integration;
- pruebas existentes;
- web UI base;
- SignalR/telemetría si ya funciona;
- SQLite si ya se utiliza correctamente.

## Reubicar conceptualmente

### Runtime actual
De:
`producto completo SoftPLC`

A:
`motor de simulación y ejecución local de Atlas IR`.

### Modbus
De:
`camino para PLC universal`

A:
`adapter de protocolo/online I/O`.

### Tanque de agua
De:
`función del producto`

A:
`SampleProject + RegressionFixture`.

### Riego automático
Si ya está construido:
`SampleProject + RegressionFixture`.

No seguir agregando demos hasta cerrar IR + validators + Target SDK.

---

# 24. CAMBIO DE RUMBO DEL DOCUMENTO ANTERIOR

La especificación anterior que define:

> “La PC ejecuta el ciclo lógico”

como misión central ya NO representa la misión completa.

Atlas sí puede ejecutar lógica localmente para simulación y SoftPLC opcional, pero el objetivo rector ahora es:

```text
ENGINEERING WORKBENCH
  + UNIVERSAL IR
  + VALIDATOR
  + SIMULATOR
  + TRANSLATORS
  + TARGET ADAPTERS
  + DEPLOYMENT ORCHESTRATOR
```

El modo SoftPLC queda como **un target más**:

`Target = Atlas Runtime`.

---

# 25. ARQUITECTURA DE PROYECTOS .NET PROPUESTA

No renombrar todo de golpe. Migrar incrementalmente.

Objetivo:

```text
src/
  AtlasPlc.Domain/
  AtlasPlc.Application/
  AtlasPlc.Infrastructure/

  AtlasPlc.Ir/
  AtlasPlc.Compiler/
  AtlasPlc.Validation/
  AtlasPlc.Simulation/
  AtlasPlc.Targets.Abstractions/

  AtlasPlc.Targets.AtlasRuntime/
  AtlasPlc.Targets.PlcOpen/
  AtlasPlc.Targets.Modbus/
  AtlasPlc.Targets.Siemens/
  AtlasPlc.Targets.Rockwell/
  AtlasPlc.Targets.Beckhoff/
  AtlasPlc.Targets.Schneider/

  AtlasPlc.Import.PlcOpen/
  AtlasPlc.Export.PlcOpen/

tests/
  AtlasPlc.Ir.Tests/
  AtlasPlc.Validation.Tests/
  AtlasPlc.Simulation.Tests/
  AtlasPlc.Compiler.Tests/
  AtlasPlc.Targets.ContractTests/
```

NO crear todos vacíos.

Crear sólo cuando una fase los necesite.

---

# 26. TARGET CONTRACT TESTS

Todos los target adapters deben pasar un contrato común.

Ejemplos:

```text
Adapter declares capabilities honestly
Unsupported operation returns UnsupportedCapability
Target profile mismatch blocks deploy
Generation is deterministic
Same IR + same profile -> same artifact hash
Build diagnostics are imported
Deploy never occurs if validation has Blocker
Deploy never occurs without explicit confirmation token
Verify result is recorded
Cancellation works
Timeout does not leave Atlas believing deploy succeeded
```

Para adapters sin hardware, usar fakes/recorded fixtures.

---

# 27. CAPABILITY-DRIVEN UI

La UI jamás debe mostrar un botón activo que el target no puede ejecutar.

Ejemplo:

```text
Rockwell CompactLogix X
Generate L5X       [Disponible]
Compile            [Requiere Studio 5000]
Deploy             [No disponible en este equipo]
Monitor online     [Disponible]
Upload full source [No soportado]
```

El usuario entiende la limitación sin saber detalles internos.

---

# 28. VERSIONES Y REPRODUCIBILIDAD

Guardar con cada generación/deploy:

- Atlas version;
- IR schema version;
- target adapter version;
- target profile version;
- vendor tool version;
- PLC model;
- firmware;
- artifact hash;
- compiler output;
- validation report hash;
- date/time;
- user confirmation;
- deployment result.

---

# 29. SEGURIDAD DE DESPLIEGUE

Antes de hablar con PLC físico:

- target explícitamente seleccionado;
- identidad verificada;
- proyecto validado;
- no Blockers;
- target compatibility PASS;
- usuario ve qué se va a modificar;
- confirmación explícita;
- timeout/cancellation;
- conexión segura cuando el target lo permita;
- no guardar password en texto;
- audit.

La primera implementación de un nuevo target debe ejecutar:

`Generate -> Verify offline`

antes de habilitar:

`Deploy physical`.

---

# 30. METODOLOGÍA DE DESARROLLO

Usar **Vertical Slice + Contract-First + Test-Driven Risk Reduction**.

NO waterfall gigante.
NO “crear 200 clases y luego probar”.

Cada slice debe recorrer:

```text
User Intent
 -> IR
 -> Validator
 -> Simulator
 -> Target Generator
 -> External Tool/Simulator
 -> Verify
```

## Slice 1 — Portable core

Proyecto mínimo:

```text
Start AND NOT Stop -> Motor
```

Demostrar:

- creación en UI;
- Atlas IR;
- diagnóstico;
- simulación;
- invariant Stop -> Motor OFF;
- export PLCopen XML;
- export ST;
- artifact hash.

## Slice 2 — primer target automatizable

Escoger target con toolchain accesible sin hardware.

Preferencia técnica:

- Schneider/CODESYS vía PLCopenXML+scripting, o
- Beckhoff TwinCAT Automation Interface, o
- Siemens PLCSIM + TIA Openness si el software está disponible.

La elección depende de licencias/software disponible en la máquina.

## Slice 3 — PLC físico

Sólo después del simulator target completo:

`create -> validate -> simulate -> generate -> compile -> deploy -> verify`.

---

# 31. ROADMAP PRIORIZADO

## P0 — REENCUADRE

Antes de agregar funciones:

1. congelar nuevas demos;
2. documentar qué código actual se conserva;
3. crear `TargetCapabilities`;
4. crear `IPlcTargetAdapter`;
5. crear skeleton de Atlas IR mínimo;
6. envolver runtime existente como target/simulator;
7. reclasificar Modbus.

Salida:
arquitectura nueva compila sin romper comportamiento actual.

## P1 — IR + PROJECT PACKAGE

Implementar:

- AtlasProject;
- variables;
- bool/int/real;
- logical expressions;
- timers básicos;
- outputs;
- interlocks;
- physical components;
- JSON versioning;
- migration tests.

## P2 — VALIDATORS + HINTS

Implementar primero 20-30 reglas de alto valor.

No usar IA para decidir PASS/FAIL.

## P3 — SCENARIO ENGINE

- virtual clock;
- input events;
- assertions;
- invariants;
- deterministic replay.

## P4 — PLCOPEN/ST

- import/export PLCopen XML;
- ST emitter;
- source maps;
- round-trip diagnostics.

## P5 — TARGET SDK + CAPABILITY MATRIX

- profile database;
- adapter registry;
- target selector UI;
- contract tests.

## P6 — PRIMER SIMULADOR/TOOLCHAIN EXTERNO COMPLETO

Debe probar:

```text
Atlas project
 -> external artifact
 -> external compile
 -> external simulation/runtime
 -> online values back into Atlas
```

## P7 — PRIMER PLC FÍSICO

Elegir UNO.

No intentar seis marcas a la vez.

## P8+ — VENDOR TARGETS

Agregar por familias y con fixtures/versiones reales.

---

# 32. CRITERIO PARA ELEGIR EL PRIMER PLC FÍSICO

Puntuar cada candidato:

```text
Hardware disponible          0-5
Software gratuito/disponible 0-5
API documentada              0-5
Offline simulator            0-5
Import/export abierto        0-5
Deploy automation            0-5
Community/examples           0-5
Costo                        0-5
```

Empezar por el de mayor puntuación.

No escoger por fama de marca.

---

# 33. DEFINICIÓN DE “UNIVERSAL”

Atlas puede llamarse universal si:

- el proyecto interno no depende de marca;
- los targets son plugins/adapters;
- la UI es capability-driven;
- el mismo proyecto puede compilarse para más de una familia cuando sus features caben;
- Atlas explica por qué un proyecto NO cabe en otro target;
- nuevos targets se agregan sin modificar el compilador central.

NO significa:

“un mismo protocolo descarga cualquier PLC”.

---

# 34. MATRIZ DE COMPATIBILIDAD POR PROYECTO

Antes de deploy mostrar:

```text
Proyecto: BandaPrincipal v12

Feature                 Atlas IR    S7-1500    CompactLogix    CX9020
BOOL                     YES         YES         YES             YES
TON                      YES         YES         YES             YES
State machine            YES         LOWER       LOWER           LOWER
Analog REAL              YES         YES         YES             YES
Custom FB                YES         YES         YES             YES
Target extension X       YES         NO          NO              NO

Resultado:
S7-1500       PASS
CompactLogix  PASS with warnings
CX9020        BLOCKED: Target extension X unsupported
```

---

# 35. IMPORTAR PROYECTO EXISTENTE

Flujo:

```text
Import file/vendor source
 -> detect format
 -> vendor parser
 -> vendor AST/model
 -> lower to Atlas IR
 -> preserve opaque nodes
 -> diagnostics
 -> simulate if semantics sufficient
```

Nunca afirmar equivalencia si hubo nodos no traducidos.

Mostrar:

```text
Imported: 93%
Opaque: 5%
Unsupported: 2%
Simulation fidelity: Partial
```

---

# 36. COMPARAR/TRADUCIR ENTRE PLCs

Función futura clave:

```text
Import Siemens project
 -> Atlas IR
 -> Compatibility Report Rockwell
 -> Translate supported subset
 -> Explain incompatible constructs
 -> Generate Rockwell artifact
```

Cada traducción debe emitir:

- exact mappings;
- approximate mappings;
- unsupported;
- vendor extensions lost;
- timing semantic differences;
- data type differences;
- task scheduling differences.

Nada silencioso.

---

# 37. “PREGÚNTALE A ATLAS”

La IA puede ayudar al usuario a formular intención:

```text
Usuario:
Quiero una bomba que llene el tanque y pare cuando se llene.

Atlas:
Necesito saber:
1. ¿Qué sensor indica nivel bajo?
2. ¿Qué sensor indica nivel alto?
3. ¿Existe protección por falta de agua?
4. ¿Hay paro de emergencia?
5. ¿La bomba debe rearmarse automáticamente?
```

La IA produce una propuesta de IR.

Después:

`deterministic validators` deciden si pasa.

Separar:

`AI suggestion` ≠ `verified logic`.

---

# 38. WHAT-NOT-TO-DO

Agente de código:

NO:

- agregar otra demo como objetivo principal;
- hardcodear lógica de tanque/riego;
- tratar Modbus como deploy universal;
- crear un proyecto .NET por cada demo;
- escribir protocolos propietarios sin especificación;
- generar archivos binarios propietarios por ingeniería inversa como camino principal;
- declarar soporte por marca sin modelo/firmware;
- decir “deploy successful” sin verify;
- esconder errores del compilador del fabricante;
- mezclar IEC 61131-3 con IEC 61499;
- afirmar safety certification;
- usar IA generativa como árbitro de seguridad;
- refactor masivo de nombres antes de estabilizar contratos;
- romper runtime actual.

---

# 39. PRIMER TRABAJO QUE DEBE HACER EL AGENTE AHORA

NO implementar todo este documento de un golpe.

Ejecutar exactamente:

## FASE A — AUDITORÍA DE REENCUADRE

1. leer repo completo;
2. identificar qué clases actuales pertenecen a:
   - IR/model;
   - simulation runtime;
   - protocol;
   - persistence;
   - UI;
   - validation;
3. crear `CURRENT_ARCHITECTURE_MAP.md`;
4. listar deuda causada por asumir “SoftPLC como producto final”;
5. proponer cambios mínimos.

NO modificar runtime todavía.

## FASE B — CONTRATOS CENTRALES

Crear únicamente:

- `TargetCapability`;
- `TargetCapabilities`;
- `TargetIdentity`;
- `TargetProfile`;
- `IPlcTargetAdapter`;
- `TargetOperationResult`;
- `UnsupportedCapabilityException` o resultado equivalente;
- tests de contratos.

Crear un adapter:

`AtlasRuntimeTargetAdapter`

que envuelva el runtime existente SIN duplicarlo.

Crear un adapter conceptual:

`ModbusOnlineAdapter`

para demostrar que Modbus es online data/I-O, NO project deployment.

## FASE C — IR MÍNIMA

Sólo:

```text
Project
Variable
Input
Output
Boolean expression
Assignment
Interlock
SafeState
```

Migrar el ejemplo Tanque a esa IR únicamente como regression fixture.

NO hacer todavía Siemens/Rockwell.

## FASE D — PRIMER VALIDATOR

Implementar:

- undefined reference;
- duplicate writer;
- output safe state;
- interlock dominance;
- contradictory boolean expression;
- unreachable branch.

Cada uno con DiagnosticId + Hint.

## FASE E — PRUEBA VERTICAL

Crear un proyecto mínimo mediante Atlas IR:

```text
Motor = Start AND NOT Stop AND GuardClosed
```

Invariante:

```text
Stop -> NOT Motor
NOT GuardClosed -> NOT Motor
```

Probarlo en runtime actual.

Sólo al pasar esto empezar PLCopen/ST.

---

# 40. GATE DE CIERRE DE CADA FASE

Antes de pasar de fase:

```text
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet publish -c Release
```

Exigir:

- 0 errores;
- 0 warnings;
- tests existentes pasan;
- tests nuevos pasan;
- no artifacts generados versionados;
- no commit automático;
- no push automático.

El agente entrega:

```text
WHAT CHANGED
WHY
FILES
TESTS
RISKS
NEXT GATE
```

Y se detiene.

---

# 41. QUÉ HACER CON EL TRABAJO MODBUS ACTUAL

CONSERVARLO.

Ya demostró valor real:

```text
External Modbus simulator
 -> Atlas input
 -> Atlas logic
 -> Atlas output
 -> external Modbus simulator
```

Eso prueba la capa de I/O/online integration.

Mover su responsabilidad arquitectónica a:

```text
AtlasPlc.Protocols.Modbus
        |
        v
Online Data / Remote I/O Adapter
```

Más adelante servirá para:

- commissioning;
- protocol testing;
- digital twins;
- generic remote I/O;
- monitor online;
- devices whose control interface is Modbus.

NO usarlo como evidencia de que Atlas “programa cualquier PLC”.

---

# 42. PRIMERAS PRUEBAS DE ACEPTACIÓN DEL PRODUCTO CORRECTO

## A — usuario sin PLC

Usuario crea:

```text
Al presionar Start prende Motor.
Stop lo apaga.
Si puerta está abierta no puede prender.
```

Atlas debe:

- construir IR;
- detectar variables necesarias;
- pedir datos faltantes;
- simular;
- probar invariantes;
- mostrar PASS.

## B — error lógico

Usuario crea:

```text
Forward = StartForward
Reverse = StartReverse
```

Plant model define motores mutuamente excluyentes.

Atlas:

```text
BLOCKER:
Forward y Reverse pueden activarse simultáneamente.

HINT:
Agrega interlock cruzado.
```

## C — error físico

```text
Pump = Auto AND Demand
```

Plant model declara `TankLow`.

Atlas:

```text
BLOCKER:
Existe un camino que activa Pump sin comprobar TankLow.

HINT:
Agrega NOT TankLow como permiso.
```

## D — target incompatible

Proyecto usa REAL 64-bit o feature no disponible en CPU elegida.

Atlas bloquea generación/deploy y explica alternativa.

## E — simulador externo

Mismo proyecto:

```text
Atlas -> target artifact -> simulator -> online verify
```

## F — PLC físico

Mismo flujo contra target físicamente disponible:

```text
Identify -> Generate -> Compile -> Confirm -> Deploy -> Verify
```

---

# 43. FUENTES TÉCNICAS QUE DEBEN GUIAR EL DISEÑO

Consultar documentación oficial y no asumir compatibilidad por marketing.

Referencias base:

- PLCopen XML / IEC 61131-10:
  https://www.plcopen.org/standards/xml-echange/

- Siemens TIA Portal Openness:
  https://docs.tia.siemens.cloud/

- Rockwell Studio 5000 import/export:
  https://www.rockwellautomation.com/

- Beckhoff TwinCAT Automation Interface:
  https://infosys.beckhoff.com/

- Schneider EcoStruxure Machine Expert PLCopenXML + scripting:
  https://product-help.schneider-electric.com/

- Eclipse 4diac:
  https://eclipse.dev/4diac/

No copiar documentación propietaria al repo.
Guardar sólo enlaces, notas propias y fixtures permitidos.

---

# 44. DECISIÓN MAESTRA

Toda decisión futura debe responder esta pregunta:

> ¿Esto ayuda a que un usuario cree un proyecto una sola vez, Atlas demuestre si tiene sentido lógico/físico y luego lo traduzca/despliegue de forma verificable al target seleccionado?

Si NO:

- no es prioridad del núcleo;
- probablemente pertenece a demo, plugin, protocolo o integración secundaria.

---

# 45. INSTRUCCIÓN FINAL AL AGENTE

Lee este documento completo antes de modificar código.

No “mejores” el alcance.
No lo conviertas otra vez en un SoftPLC monolítico.
No agregues demos.
No intentes soportar todas las marcas en una sola iteración.

Primero construye la columna vertebral:

```text
PROJECT
  -> ATLAS IR
  -> VALIDATION + HINTS
  -> SIMULATION
  -> TARGET CAPABILITY CHECK
  -> TARGET ADAPTER
  -> GENERATE
  -> BUILD
  -> DEPLOY
  -> VERIFY
```

El trabajo existente debe reutilizarse donde encaje.

**NO COMMIT.**
**NO PUSH.**
Detente al terminar FASE A + propuesta de cambios mínimos y entrega el informe para revisión.
