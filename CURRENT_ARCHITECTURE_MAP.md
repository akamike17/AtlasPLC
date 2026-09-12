# ATLASPLC — CURRENT ARCHITECTURE MAP (FASE A · Auditoría de reencuadre)

**Documento rector aplicado:** `ATLASPLC_UNIVERSAL_ESPECIFICACION_MAESTRA.md` (sección 39, FASE A).
**Fecha:** 2026-09-11
**Alcance:** SOLO LECTURA. No se modificó runtime ni código de producción. No commit, no push.

---

## 1. Inventario de proyectos .NET

Solución: `AtlasSoftPlc.slnx` (formato slnx).

| Proyecto | Capa / rol | Nota |
|---|---|---|
| `src/AtlasSoftPlc.Domain` | Dominio (entidades + enums) | IR parcial + Plant parcial |
| `src/AtlasSoftPlc.Application` | Aplicación (servicios + lógica) | IntentParser/LogicBuilder/Validators |
| `src/AtlasSoftPlc.Infrastructure` | Persistencia (SQLite) | Repos + SchemaMigrator |
| `src/AtlasSoftPlc.Protocols.Modbus` | Protocolo | NModbus TCP driver/server |
| `src/AtlasSoftPlc.Runtime` | Runtime / simulación | ScanCoordinator, arbiter, watchdog |
| `AtlasSoftPlc.Web` (raíz) | UI (MVC + SignalR) | Controllers, Auth, Hubs, ModbusIoService |
| `tests/*` (7 proyectos) | Tests | Web.Tests **roto → no compila** |

Namespace de los proyectos `src/*`: `AtlasSoftPlc.*`. El web usa `AtlasSoftPlc.Web.*`.
Existen artefactos `obj/` bajo varios proyectos que no deberían versionarse (deuda de `.gitignore`).

---

## 2. Mapa de clases por capa (según el criterio FASE A punto 2)

### IR / Modelo (lo más cercano a "Atlas IR" del spec)
- `Domain/Logic/LogicProgram.cs` → `LogicProgram` (raíz) + `LogicRule`.
- `Domain/Logic/ExpressionNodes.cs` → `ExpressionNode` (abstract, `[JsonDerivedType]` polimórfico) y subclases: `Constant`, `Variable`, `Not`, `And`, `Or`, `Compare`, `Arithmetic`, `Edge`, `TimerState`, `CounterState`.
- `Domain/Logic/LogicActions.cs` → `LogicAction` polimórfico (SetOutput, SetMemory, ResetMemory, timers, counters, alarmas, log).
- `Domain/Projects/Project.cs` → `Project` + `ProgramVersion`.
- `Domain/Projects/PlcProgramDefinition.cs` → `PlcProgramDefinition` (variables + lógica + failsafe + ModbusMap).
- `Domain/Variables/VariableDefinition.cs` → `VariableDefinition`.
- `Domain/Values/PlcValue.cs` → unión tipada de valores PLC.

**Evaluación IR:** la IR ya es polimórfica, serializable (JSON camelCase + enums string) y versionable. **PERO** está fusionada con el modelo de runtime (failsafe, ModbusMap dentro de `PlcProgramDefinition`) y no incluye las entidades mínimas del spec §2.1 (StateMachine, Transition, InterlockRule, PhysicalConstraint, ScenarioDefinition, etc.).

### Simulation runtime (el spec manda CONSERVAR y redefinir como "Atlas Simulation Runtime")
- `Runtime/Hosting/PlcRuntimeService.cs` → BackgroundService, single-writer, scan loop, watchdog, anti-stale generation.
- `Runtime/Engine/ScanCoordinator.cs` → coordina captura → ejecución → arbitraje.
- `Runtime/Engine/LogicExecutor.cs` → evalúa IR → `OutputProposal`.
- `Runtime/Engine/OutputArbiter.cs` → arbitraje por prioridad + interlocks + failsafe.
- `Runtime/Engine/ScanExpressionContext.cs`, `ScanRequest.cs`, `TimerCounterManagers.cs`.
- `Runtime/Expressions/ExpressionEngine.cs`, `IExpressionContext.cs`.
- `Runtime/Hosting/RuntimeState.cs` → `RuntimeStateStore`, `RuntimeSnapshot`, `WatchdogService`.
- `Runtime/Hosting/FailsafePolicy.cs` → política failsafe por tipo.
- `Runtime/Virtual/VirtualDeviceDriver.cs`, `Snapshots/Snapshots.cs`.
- `Domain/Runtime/*` → `RuntimeModels` (Interlock, OutputProposal, OutputPriority), `TimerCounterStates`, `IRuntimeNotifier`, `IRuntimeAuditSink`.

**Evaluación:** sólido, determinista, con failsafe/watchdog/anti-stale bien implementado. Esto es exactamente lo que el spec §8/§23 manda conservar. **Deficiencia:** no hay clock virtual / single-step determinista para tests (usa `PeriodicTimer` tiempo real), ni scenario runner.

### Protocol
- `Protocols/Modbus/*` → `ModbusTcpDriver` (IDeviceDriver), `ModbusTcpServer`, `ModbusAddress`, `RegisterConverter`, `CircuitBreaker`, `ModbusEndianness`.
- `Web/Services/ModbusIoService.cs` → puente I/O Modbus ↔ runtime.
- `Web/Services/ModbusOptions.cs` → configuración.

**Evaluación:** implementación Modbus TCP madura y con circuit-breaker. **PERO** el spec §14.8 exige reclasificarla conceptualmente de "deploy universal" a "Protocol/OnlineData Adapter". Hoy `ModbusIoService` está en `Web/Services`, no en una capa de protocolo pura.

### Persistence
- `Infrastructure/Persistence/*` → `SqliteStore` + `SchemaMigrator` + repos (`SqliteRepositories`, `SqliteDomainRepositories`, `SqlitePlcProgramRepository`) + `AtlasJson`.

**Evaluación:** SQLite versionado con migraciones. Bien. No hay aún "Project Package" (`.atlasplc` ZIP) del spec §20.

### UI
- `Controllers/*` (Home, Runtime, Explainer, Account) — MVC.
- `Hubs/RuntimeHub.cs` — SignalR.
- `Auth/*` — Argon2id, roles, lockout, seeder.
- `Services/SimulationService.cs` — biblioteca + bootstrap + inputs/outputs UI.
- `Views/*`, `wwwroot/*` — Razor + estáticos.

**Evaluación:** UI capability-driven NO existe aún (spec §27). Los botones no se derivan de `TargetCapabilities`.

### Validation
- `Application/Validation/ValidationService.cs` → `ValidationService` + `ReferencesValidationRule` + `FailsafeValidationRule`.
- `Application/Validation/ValidationModels.cs` → `ValidationIssue`, `ValidationReport`, `IValidationRule`, `ValidationContext`.

**Evaluación:** quebrantado del spec §4. El spec exige cadena de validadores (Schema/Type/Reference/Logic/ControlFlow/Timing/Physical/Safety/Target/Deployment) con diagnosticos `{DiagnosticId, Severity(Info|Warning|Error|Blocker), Category, ElementId, Message, Why, Evidence, Hint, AutoFixAvailable, TargetSpecific, RuleVersion}`. Hoy solo hay 2 reglas (`REF`, `FAILSAFE`) con severidad `Info/Warning/Error` (sin `Blocker`), sin `Why`/`Evidence`/`Hint`, sin `AutoFixAvailable`. Es la brecha más grande del pipeline.

### Business logic (application)
- `Application/Logic/IntentParser.cs` → lenguaje natural → `AutomationIntent` (heurístico).
- `Application/Logic/LogicBuilder.cs` → `AutomationIntent` → `LogicProgram`.
- `Application/Logic/Explainer.cs` → explicación humana de reglas.
- `Application/Logic/TestGenerator.cs` → casos de prueba.
- `Application/Services/*` (ProjectService, VariableService, PlcProgramService, PlcProgramCatalog, AuditService, AlarmService, HistorianService, ProgramVersionService, ConfigurationHasher).

**Evaluación:** `IntentParser`/`LogicBuilder` implementan el flujo "lenguaje natural → IR" del spec §1 de forma determinista (correcto, NO usa IA para decidir). `PlcProgramCatalog` hardcodea demos Tanque/Riego (§0 las llama fixtures; correcto como fixtures, PERO el spec prohíbe seguir agregando demos).

---

## 3. Deuda causada por "SoftPLC como producto final"

1. **El runtime sigue siendo el centro de identidad.** Program.cs registra y arranca `PlcRuntimeService` como servicio principal. El spec §24 manda reposicionarlo como *un target más* (`Target = Atlas Runtime`), no el producto.

2. **Modbus está acoplado al web y conceptualmente como I/O del SoftPLC.** `ModbusIoService` vive en `Web/Services` y bootstrapa el demo Tanque. El spec §14.8/§41 lo reclasifica a `Protocol/OnlineData Adapter`.

3. **No existe `TargetCapabilities` / `IPlcTargetAdapter` / `Target Profile DB`** (spec FASE B / §11 / §13). Los `DriverCapabilities` actuales son flags de I/O (`CanRead/CanWrite/...`), no capabilities de engineering (`Generate/Compile/Deploy/Verify`).

4. **Validation es mínima** (2 reglas vs. 20-30 de alto valor del spec §5/§6). Sin pipeline de validadores, sin `Blocker`, sin `Hint Engine` determinista (§7).

5. **No hay Plant Model** (§3). Sin `Motor`/`Pump`/`Valve` con `requires`/`mutuallyExclusiveWith`/`safeState`, es imposible detectar fallas físicas.

6. **No hay Scenario Engine / invariantes** (§9), ni clock virtual para replay determinista (§8).

7. **No hay traducción / target generators** (§10 PLCopen/ST, §14 vendors) ni Project Package (§20).

8. **Demos hardcodeadas en `PlcProgramCatalog`** (Tanque, Riego) — correctas como fixtures, pero el spec §38 prohíbe tratar demos como objetivo principal.

9. **`RuntimeMode`/`DriverProtocol` anticipan vendors** (`SiemensS7`, `RockwellCip`, `OpcUa`, `Mqtt`) sin implementación — vestigio del sueño "universal via protocolo".

10. **Mixed responsibility en `PlcProgramDefinition`**: mezcla IR (`Logic`), plant parcial (`Failsafe`, `ModbusMap`) y persistencia en un solo tipo.

---

## 4. ESTADO DEL GATE (crítico — parche urgente)

```
dotnet build -c Release  →  FAIL (3 errores, 1 warning)
```

Los proyectos `src/*` y `AtlasSoftPlc.Web` **compilan correctamente**. El gate se rompe en el proyecto de tests `AtlasSoftPlc.Web.Tests`, que quedó desconectado de dos refactors recientes:

| Archivo test | Problema | Causa raíz |
|---|---|---|
| `tests/AtlasSoftPlc.Web.Tests/SimulationServiceTests.cs:31` | `new SimulationService(runtime)` sin 2º arg | `SimulationService` ahora exige `PlcProgramService catalogService` |
| `tests/AtlasSoftPlc.Web.Tests/ModbusIoServiceTests.cs:31,46` | mismo error + `ModbusOptions.Map` no existe | `Map` fue movido de `ModbusOptions` a `PlcProgramDefinition.ModbusMap` |
| `SimulationServiceTests.cs:55` | warning CS8602 (deref nula) | `p2.Name` tras `BootstrapTankDemo` |

**Evidencia cruda del build:**
```
error CS7036: No se ha dado ningún argumento que corresponda al parámetro requerido
              "catalogService" de "SimulationService.SimulationService(PlcRuntimeService, PlcProgramService)"
error CS0117: 'ModbusOptions' no contiene una definición para 'Map'
```

Esto significa que **el pipeline de CI (spec §40: build/test/publish 0 errores) ya no se cumple** y debe corregirse como parche urgente antes de cualquier FASE B/C/D/E.

---

## 5. Cambios mínimos propuestos (SIN tocar runtime)

### Parche urgente (ordena el gate ANTES de cualquier arquitectura nueva)
1. **Arreglar `AtlasSoftPlc.Web.Tests`** para que vuelva a compilar contra las firmas actuales:
   - `SimulationServiceTests`: construir un `PlcProgramService` real sobre un `IPlcProgramRepository` en memoria (o inyectar un stub) y pasarlo a `SimulationService`.
   - `ModbusIoServiceTests`: eliminar `Map` de `ModbusOptions` (se resolve ahora desde `SimulationService.ActiveModbusMap`) y crear el `PlcProgramService` para el `SimulationService`.
   - Corregir el warning CS8602 (guard nula).

### FASE A.5 — propuesta de reencuadre (mínima, orden de ejecución)
2. **Congelar demos** (§31 P0.1): no agregar más entradas a `PlcProgramCatalog`.
3. **FASE B** — contratos centrales SOLO: `TargetCapability`, `TargetCapabilities`, `TargetIdentity`, `TargetProfile`, `IPlcTargetAdapter`, `TargetOperationResult`, `UnsupportedCapability`, + tests de contrato. Crear `AtlasRuntimeTargetAdapter` que envuelva el `PlcRuntimeService` SIN duplicarlo, y `ModbusOnlineAdapter` conceptual.
4. **FASE C** — IR mínima (Project/Variable/Input/Output/BooleanExpression/Assignment/Interlock/SafeState) y migrar Tanque como fixture de regresión.
5. **FASE D** — primer validator (undefined ref, duplicate writer, output safe state, interlock dominance, contradictory expr, unreachable branch) cada uno con `DiagnosticId + Hint`.
6. **FASE E** — prueba vertical `Motor = Start AND NOT Stop AND GuardClosed` con invariante `Stop -> NOT Motor`.

**Regla de hierro (spec §45):** no refactor masivo de nombres antes de estabilizar contratos; no romper el runtime; el runtime se conserva y se envuelve, no se reescribe.

---

## 6. Decisión maestra aplicada

Cada cambio futuro debe responder: *¿ayuda a que un usuario cree el proyecto una vez, Atlas demuestre sentido lógico/físico y lo traduzca/despliegue de forma verificable al target?* (spec §44).

---

**NO COMMIT. NO PUSH.**

---

## FASE B — Contratos centrales de target (COMPLETADA)

Nuevo proyecto `src/AtlasSoftPlc.Targets.Abstractions` + adapters + contract tests.

### Contratos (spec §11/§12/§13/§39 FASE B)
| Tipo | Archivo | Propósito |
|---|---|---|
| `TargetCapability` (enum) | `TargetCapabilities.cs` | 24 capacidades declarativas (Discover, ReadLiveData, … VerifyDeployment, Rollback) |
| `TargetCapabilities` | `TargetCapabilities.cs` | conjunto inmutable de capacidades; UI capability-driven |
| `TargetIdentity` | `TargetProfile.cs` | marca/familia/modelo/firmware (nunca "marca genérica") |
| `TargetSupportLevel` (enum) | `TargetProfile.cs` | L0..L6 por target |
| `TargetProfile` | `TargetProfile.cs` | perfil + `InferLevel(caps)` |
| `TargetOperationResult` / `VerifyResult` / `CompatibilityReport` | `TargetOperationResult.cs` | resultados tipados, sin "éxito simulado" |
| `UnsupportedCapabilityException` | `UnsupportedCapabilityException.cs` | capacidad no soportada |
| `IPlcTargetAdapter` + `PlcTargetAdapterBase` | `IPlcTargetAdapter.cs` | contrato estable; default = Unsupported |

### Adapters (envuelven lo existente SIN duplicar)
- `AtlasRuntimeTargetAdapter` (`Runtime/Targets/`) → target "Atlas Runtime": declara `Simulate/ReadLiveData/WriteLiveData/ReadSymbols`; NO `Generate/Deploy`. `GenerateAsync` = instalar config + `ResumeCommand`.
- `ModbusOnlineAdapter` (`Protocols.Modbus/Targets/`) → reclasificación §14.8/§41: declara SOLO `Discover/ReadLiveData/WriteLiveData/ReadSymbols`. `ValidateAsync` BLOQUEA todo proyecto (Modbus no programa PLCs); `GenerateAsync` siempre Unsupported.

### Contract tests (spec §26) — `tests/AtlasSoftPlc.Targets.ContractTests` (13 tests)
Declaración honesta, InferLevel L1/L3/L5/L6, `IsFullySpecified`, deploy sin token → fail, Modbus bloquea ingeniería, Generate Unsupported, excepción carga capacidad.

### Cambios de solución
- `AtlasSoftPlc.slnx`: + `AtlasSoftPlc.Targets.Abstractions` + `AtlasSoftPlc.Targets.ContractTests`.
- `Runtime.csproj` y `Protocols.Modbus.csproj`: referencia a `Targets.Abstractions`.

### Gate verificados
`build Release` 0 errores/0 warnings · `test Release` 462 tests 0 fallos.

---

## FASE C — IR mínima canónica (COMPLETADA)

- `SafeState` (`Domain/Ir/SafeState.cs`): estado seguro como entidad de primera clase (hoy era solo `Dictionary<Guid,PlcValue>`).
- `AtlasIrDocument` (`Domain/Ir/AtlasIrDocument.cs`): raíz canónica de la IR (§2) que unifica Project + Variables + Logic + Interlocks + SafeStates. Serializable, `IsValid()`, `ToProgramDefinition()` (SafeStates→Failsafe).
- `AtlasIrFixtures` (`Application/Ir/AtlasIrFixtures.cs`): fixtures de regresión sobre la IR canónica — `BuildMotorStopGuard()` (columna vertebral FASE E: `Motor = Start AND NOT Stop AND GuardClosed`) y `BuildTank()` (migración 1:1 del demo Tanque).
- Tests (`AtlasIrDocumentTests.cs`, 8): validez, claves únicas, SafeState→Failsafe, round-trip JSON, equivalencia 1:1 con `PlcProgramCatalog.BuildTankDemo()`.

Gate: build 0 errores/0 warnings · test 470 tests 0 fallos (Application.Tests 49→57).

---

## FASE D — Validators deterministas (COMPLETADA)

Extiende el modelo de validación a `DiagnosticId + Category + Why + Evidence + Hint` (spec §4) y añade severidad `Blocker` (el reporte invalida con `Error o Blocker`).

6 reglas nuevas (`Application/Validation/FaseDValidationRules.cs`), cada una con `DiagnosticId + Hint`:

| Regla | DiagnosticId | Severidad |
|---|---|---|
| UndefinedReferenceValidationRule | ATLAS-REF-0001/0002 | Blocker |
| DuplicateWriterValidationRule | ATLAS-LOGIC-0003 | Blocker |
| OutputSafeStateValidationRule (sin escritor / SafetyCritical) | ATLAS-LOGIC-0005 / ATLAS-SAFE-0004 | Blocker / Warning |
| InterlockDominanceValidationRule (stop domina start) | ATLAS-SAFE-0006 | Warning |
| ContradictoryExpressionValidationRule (X ∧ ¬X / X ∨ ¬X) | ATLAS-LOGIC-0007/0008 | Blocker / Warning |
| UnreachableBranchValidationRule (rama muerta) | ATLAS-LOGIC-0009 | Blocker |

Registrados en DI (`Program.cs`). 11 tests nuevos (`FaseDValidationRulesTests.cs`).

## FASE E — Prueba vertical (COMPLETADA)

- `VerticalSliceTests` (`Application/Ir/AtlasIrFixtures.BuildMotorStopGuard()` + `ScanCoordinator` real) ejecuta `Motor = Start AND NOT Stop AND GuardClosed` y verifica los invariantes del spec §39 FASE E:
  - `Stop → NOT Motor`
  - `NOT GuardClosed → NOT Motor`
- 4 tests: caso feliz, ambos invariantes, sin errores de scan.
- `IntegrationTests.csproj` referencia ahora Application (para el fixture).

Gate final: `build Release` 0 errores/0 warnings · `test Release` 485 tests 0 fallos.

### Resumen de fases auditadas (A→E completadas)
FASE A (mapa) → FASE B (contratos target) → FASE C (IR mínima) → FASE D (validators) → FASE E (prueba vertical). Cierre del ciclo conforme §45: NO commit, NO push.