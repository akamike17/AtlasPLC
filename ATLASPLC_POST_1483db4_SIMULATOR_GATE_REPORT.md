# AtlasPLC — Simulator Gate Report

## Baseline and result

- Initial and final commit reference: `1483db4486784a4806397d2fd08a9d8c4d6694f5`
- Working tree remains uncommitted by instruction.
- Branch: `codex/web-suite-modbus-review`

## WHAT CHANGED

- `AtlasIrSimulationPipeline`: canonical IR validation, lowering, hash assignment and adapter simulation gate.
- `PlantModel` plus `PlantModelValidationRule`: requirements, mutual exclusion, safe state and missing reference blockers.
- `AtlasIrDocument`, `PlcProgramDefinition` and `CanonicalProgramHasher`: Plant Model is carried through lowering and included deterministically in the semantic hash.
- `ScenarioEngine`: virtual clock, input events, assertions and deterministic traces without replacing the production timer.
- `IExternalSimulatorAdapter`: external laboratory contract with no fake implementation.
- Project references and focused P0 tests.

## WHY

- P0-1: enforces `AtlasIrDocument -> validate -> lower -> SimulateAsync` and blocks invalid IR before the target.
- P0-2: adds the minimum physical constraints required for static reasoning.
- P0-3: adds reproducible temporal scenarios.
- P0-4: defines the external simulator boundary without claiming unavailable capabilities.

## Tests and gates

Focused P0 tests passed. Full suite with `RunConfiguration.DisableParallelization=true`: **526 passed, 0 failed, 0 skipped**.

Required commands completed successfully:

```text
dotnet restore AtlasSoftPlc.slnx
dotnet build AtlasSoftPlc.slnx -c Release        # 0 warnings, 0 errors
dotnet test AtlasSoftPlc.slnx -c Release --no-build
dotnet publish AtlasSoftPlc.Web.csproj -c Release --no-build
dotnet list AtlasSoftPlc.slnx package --vulnerable --include-transitive
git diff --check
```

NuGet vulnerability scanning reported no vulnerable packages from configured sources; no High/Critical finding was reported.

## External simulator gate

ModRSsim2 was found running as an external process: `ModRSsim2.exe`, PID `24876`, listening on `0.0.0.0:502`. A real probe used the repository `ModbusTcpDriver` against `127.0.0.1:502`, unit 1, coil 10: connect succeeded; initial read was `False`; write of `True` succeeded; readback was `True`; disconnect and reconnect both succeeded; post-reconnect read remained `True`. This validates external Modbus process I/O and reconnect behavior.

ModRSsim2 is treated only as an external Modbus device for Online Data / Remote I/O. It is not treated as a project PLC simulator or programming target. No vendor PLC deployment was attempted.

## Readiness

| Subsystem | State |
|---|---|
| Atlas IR canonical pipeline | READY for automated validation/lowering gate |
| Plant Model validators | READY for automated static checks |
| Virtual scenario engine | READY for deterministic unit scenarios |
| External simulator contract | READY as contract; no generic fake implementation |
| External Modbus process E2E | READY for the verified operator-selected ModRSsim2 endpoint |
| Physical PLC deployment | NOT READY BY DESIGN |
| Vendor adapters | NOT STARTED BY DESIGN |
| Vendor toolchain compilation | NOT READY |
| Physical online verification | NOT READY BY DESIGN |
| Safety/SIL certification | NOT READY BY DESIGN |

## Residual risks

- The external process test depends on the operator-selected ModRSsim2 endpoint and its coil map; the verified endpoint was local port 502, unit 1, coil 10.
- Plant validation covers declared static requirements and mutual-exclusion paths; it is not safety certification or a complete physical solver.
- PLCopen/ST, capability-driven UI completion and `.atlasplc` packaging remain the next iteration.

## Exact files modified

```text
src/AtlasSoftPlc.Application/AtlasSoftPlc.Application.csproj
src/AtlasSoftPlc.Application/Services/AtlasIrSimulationPipeline.cs
src/AtlasSoftPlc.Application/Simulation/ScenarioEngine.cs
src/AtlasSoftPlc.Application/Validation/PlantModelValidationRule.cs
src/AtlasSoftPlc.Domain/Ir/AtlasIrDocument.cs
src/AtlasSoftPlc.Domain/Ir/PlantModel.cs
src/AtlasSoftPlc.Domain/Projects/CanonicalProgramHasher.cs
src/AtlasSoftPlc.Domain/Projects/PlcProgramDefinition.cs
src/AtlasSoftPlc.Targets.Abstractions/IExternalSimulatorAdapter.cs
tests/AtlasSoftPlc.Application.Tests/AtlasSoftPlc.Application.Tests.csproj
tests/AtlasSoftPlc.Application.Tests/P0PipelinePlantScenarioTests.cs
ATLASPLC_POST_1483db4_SIMULATOR_GATE_REPORT.md
```
