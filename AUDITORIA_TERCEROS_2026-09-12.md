# ATLASPLC — AUDITORÍA INDEPENDIENTE DE TERCEROS (INTERNA + EXTERNA + REGULATORIA)

**Fecha:** 2026-09-12
**Alcance:** repo local `C:\Users\Admin\source\repos\AtlasPLC` (código + dependencias + configuración + posture regulatoria).
**Método:** SOLO LECTURA. Evidencia en disco con herramientas reales (`dotnet build/test`, `dotnet list package --vulnerable/--deprecated/--outdated --include-transitive`). Valores duros, no estimaciones. No se modificó código de producción.
**Postura:** tercera parte independiente — no soy autor del código; reporto hallazgos con severidad y evidencia concreta (archivo:línea).

---

## 0. RESUMEN EJECUTIVO

| Auditoría | Veredicto | Hallazgo que bloquea limpio |
|---|---|---|
| **Interna (calidad/arquitectura)** | Aprobada con observaciones | Config muerta `Modbus:Map` (INT-01) |
| **Externa (proveedores/dependencias)** | **Con hallazgo ALTO** | 2 CVE High transitivos en pila de TEST (EXT-01) |
| **Regulatoria** | Conforme, con matizaciones | Declaración correcta "NO Safety PLC"; trazabilidad SafetyCritical por cerrar |
| **Independiente/gobernanza** | Sin bloqueos | Higiene de árbol + notices desactualizados |

**Verdad dura clave descubierta esta ronda:** el escaneo `dotnet list ... --vulnerable` SIN `--include-transitive` reporta **falsamente "0 vulnerables"** (solo chequea referencias directas). Con `--include-transitive` reaparecen los 2 CVE. El hallazgo de la auditoría anterior **sigue vigente y sin remediar**.

---

## 1. AUDITORÍA INTERNA (calidad de software)

### 1.1 Métricas duras (verificadas en esta ronda)
- **15 proyectos** .NET 8: 8 `src` + 7 `tests` (solución `AtlasSoftPlc.slnx`).
- **113 archivos `.cs`** (sin `bin/`/`obj/`), 80 archivos de test.
- `dotnet build AtlasSoftPlc.slnx -c Release` → **0 errores, 0 warnings** (verificado).
- `dotnet test AtlasSoftPlc.slnx -c Release` → **486 tests, 0 fallos** (Domain 21 · Application 68 · Infra 49 · Modbus 112 · Runtime 172 · Web 43 · Targets 13 · Integration 8).
  - Nota: el total subió de 485 (auditoría anterior) a **486** por 1 test nuevo ya integrado.
- 2 marcadores `TODO`/`FIXME` (ambos inocuos: comentarios doc, no deuda).

### 1.2 Arquitectura — veredicto
Separación limpia por capas (Domain / Application / Infrastructure / Runtime / Protocols.Modbus / Targets.Abstractions / Web), sin dependencias inversas. Runtime con single-writer, watchdog independiente con timer propio, failsafe lock-free (generación/epoch anti-stale, pruebado contra deadlock/late-scan), arbitraje por prioridad determinista. **Sin bandera roja de concurrencia.** FASE A–E del spec maestro completadas y gate verde (ver `CURRENT_ARCHITECTURE_MAP.md`).

### 1.3 Hallazgos internos

| ID | Severidad | Hallazgo | Evidencia | Recomendación |
|---|---|---|---|---|
| **INT-01** | Media | Config muerta: `appsettings.json` declara `Modbus:Map` pero `ModbusOptions` (Web/Services) ya NO tiene propiedad `Map`. El mapa real viene de `PlcProgramDefinition.ModbusMap` vía `SimulationService.ActiveModbusMap`. Binding huérfano. | `appsettings.json:24-28` vs `Services/ModbusOptions.cs` (sin `Map`) vs `Services/ModbusIoService.cs:70` (`ResolveModbusMap()` → `_simulation.ActiveModbusMap`). | Eliminar bloque `Map` de `appsettings.json` o documentar explícitamente el cambio de fuente. |
| **INT-02** | Media | `Enabled:true` para Modbus en `appsettings.json` por defecto. El puente arranca en cada ejecución e intenta conectar a `127.0.0.1:502`. En un host sin dispositivo eso es ruido de logs + backoff continuo. | `appsettings.json:16`. | Evaluar `Enabled:false` por defecto en config base y activar solo en entornos de I/O real. |
| **INT-03** | Baja | `UserSeeder` no siembra nada en la config base: `appsettings.json` no trae `Auth:Seed` ni `Auth:SeedPassword` (solo están, aparentemente, en `appsettings.Development.json`). Sin semilla → nadie puede loguearse en producción salvo seed manual. Correcto desde seguridad (sin credenciales hardcodeadas) pero operativamente a documentar. | `Auth/UserSeeder.cs:32-45` + `appsettings.json` (sin bloque `Auth:Seed`). | Documentar en README el flujo de primer arranque (cómo crear el admin inicial). |
| INT-04 | Informativa | 2 `TODO` son comentarios doc, no deuda. | `Program.cs:49`, `tests/.../TargetContractTests.cs:9`. | — |

---

## 2. AUDITORÍA EXTERNA (proveedores / dependencias)

### 2.1 Inventario directo (PackageReference, verificado)

| Paquete | Versión | Licencia | Rol |
|---|---|---|---|
| Microsoft.Data.Sqlite | **10.0.12** | MIT | Persistencia |
| Microsoft.Extensions.Hosting.Abstractions | 8.0.0 | MIT | BackgroundService |
| Microsoft.Extensions.Logging.Abstractions | 8.0.0 | MIT | Logging |
| Microsoft.AspNetCore.Mvc.Testing | 8.0.0 | MIT | Tests |
| Konscious.Security.Cryptography.Argon2 | 1.3.1 | MIT | Hash contraseñas |
| Serilog.AspNetCore | 10.0.0 | Apache-2.0 | Logging |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | Logging |
| NModbus / NModbus.Serial | 3.0.83 | MIT | Modbus TCP/RTU |
| xunit / runner / coverlet.collector / Microsoft.NET.Test.Sdk | 2.5.3 / 6.0.0 / 17.8.0 | MIT | Tests |

### 2.2 Hallazgo ALTO — 2 CVE en pila de TEST (EXT-01)

`dotnet list <proj> package --vulnerable --include-transitive`:

| Paquete transitivo | Resuelto | Severidad | Advisory | Alcance |
|---|---|---|---|---|
| System.Net.Http | 4.3.0 | **High** | GHSA-7jgj-8wvc-jh57 | test-only |
| System.Text.RegularExpressions | 4.3.0 | **High** | GHSA-cmhx-cq75-c4mj | test-only |

**Evidencia (documentada contra supuesto):**
- Verificado en `tests/AtlasSoftPlc.Web.Tests` con `--include-transitive` → ambos CVE presentes.
- Los 8 proyectos `src/*` (producción) escaneados con `--include-transitive` → **0 vulnerables**. Confirmado en Domain, Infrastructure, Runtime (y el resto vía escaneo completo).
- **Causa:** la cadena de dependencias de `xunit 2.5.3` / `Microsoft.NET.Test.Sdk 17.8.0` / `coverlet.collector 6.0.0` arrastra `System.Net.Http 4.3.0` (netstandard1.3, obsoleta) y `System.Text.RegularExpressions 4.3.0`.

**Impacto real:** solo afecta a proyectos de test, NO al runtime de producción. Riesgo operativo bajo, pero **rompe una auditoría de cumplimiento limpia** (los advisories son High).

**Corrección:** actualizar pila de test a versiones actuales — `xunit 2.9.3+`, `Microsoft.NET.Test.Sdk 17.10+` (o 18.x), `coverlet.collector 10.0.1`, o forzar `<PackageReference>` directos a versiones seguras de los dos paquetes transitivos.

### 2.3 Version skew — paquete fuera de banda

**Hallazgo de consistencia (EXT-02):** todo el stack es `net8.0` (15/15 proyectos), pero `Microsoft.Data.Sqlite` está en **10.0.12** (banda .NET 10). Compila y no tiene CVE, pero es un sesgo de versión que mezcla el runtime de datos de una generación posterior con un host net8. Recomendación: bajar a la rama 8.x de `Microsoft.Data.Sqlite` (8.0.x) para coherencia de toolchain, o asumirlo de forma documentada (referencia explícita a por qué 10.x corre sobre net8).

### 2.4 Deprecados / obsoletos

- `xunit 2.5.3` marcado `Legacy` (alternativa `xunit.v3`) en los 7 proyectos de test.
- `Microsoft.Data.Sqlite 10.0.12` listado como desactualizado (`10.0.12` vs rama actual) — coherente con EXT-02.

---

## 3. AUDITORÍA REGULATORIA

### 3.1 Contexto normativo
- **Seguridad funcional:** IEC 61508 / IEC 61511 / ISO 13849-1 — un PLC estándar NO es safety PLC certificado.
- **Ciberseguridad industrial:** IEC 62443 (marco de referencia para hardening/segmentación).

### 3.2 Postura de seguridad de aplicación (verificada en código)

| Control | Estado | Evidencia |
|---|---|---|
| FallbackPolicy global (todo requiere auth) | ✅ Correcto | `Program.cs:51-56` |
| Antiforgery con header `X-CSRF-TOKEN` | ✅ Correcto | `Program.cs:44-47`, `RuntimeController.cs:47-55` |
| Contraseñas Argon2id (no texto plano, no PBKDF2 legacy) | ✅ Correcto | `PasswordHasher`+`Konscious...Argon2` |
| Lockout por usuario + rate-limit por IP anti fuerza bruta | ✅ Correcto | `AuthService.cs:39-95` (lockout sin bug de "primer fallo bloquea") |
| Cookie `HttpOnly` + `SameSite=Strict` + `SecurePolicy=Always` (prod) | ✅ Correcto | `Program.cs:170-176` |
| Cabeceras seguridad (nosniff, frame DENY, CSP, Referrer-Policy, Permissions-Policy) | ✅ Correcto | `Program.cs:203-219` |
| IDOR: `TrySetInput` rechaza variables inexistentes → 404 | ✅ Correcto | `RuntimeController.cs:57-61` |
| Mutaciones solo POST + `[Authorize]` + roles (`Administrator,Operator`) | ✅ Correcto | `RuntimeController.cs:64-94` |
| HSTS en producción | ✅ Correcto | `Program.cs:186-188` |

### 3.3 Hallazgos regulatorios

| ID | Severidad | Hallazgo |
|---|---|---|
| REG-01 | Conforme | El producto declara EXPLÍCITAMENTE que NO es SIL-rated, no sustituye E-Stop ni relé de seguridad (README + `FailsafeValidationRule`). Correcto — evita responsabilidad regulatoria falsa. |
| REG-02 | Media | `SafetyCritical=true` emite solo `Warning` (no Blocker). Debe quedar documentado que la marcación es INFORMATIVA y no certifica nada (el mensaje ya lo dice, pero conviene reforzar en README). |
| REG-03 | Baja | Sin audit trail formal que vincule cada regla `SafetyCritical` a una evaluación de riesgo previa (para IEC 62443 / ISO 13849). |
| REG-04 | Informativa | Hoy NO hay despliegue a PLC físico (FASE A–E sin vendors Siemens/Rockwell/Beckhoff). Exposición regulatoria actual = simulación + online-data (Modbus) únicamente. Al añadir vendors, obligatorio: matriz de capacidades por target + verificación post-deploy (ya diseñado en spec §19/§29). |

---

## 4. AUDITORÍA INDEPENDIENTE / DE TERCEROS (gobernanza)

| ID | Severidad | Hallazgo | Evidencia |
|---|---|---|---|
| IND-01 | Media | `THIRD_PARTY_NOTICES.md` **desactualizado**: dice `Microsoft.Data.Sqlite 9.x` pero el csproj usa `10.0.12`; NO lista Argon2 ni Serilog (que sí están en el producto). Obligación de licencias MIT/Apache exige exactitud. | `THIRD_PARTY_NOTICES.md:5` vs csprojs. |
| IND-02 | Baja | Árbol de trabajo sucio: carpeta `no commit/` (5 .md), `Nuevo documento de texto.txt` (0 bytes), specs borradas marcadas `D` en git. Riesgo de fuente de verdad fragmentada. | `git status` + `find`. |
| IND-03 | Baja | `.slnx` (formato SDK 10) sobre proyectos net8.0 — en un entorno con solo SDK 8 podría no abrir. Verificar toolchain (README ya advierte). | `AtlasSoftPlc.slnx`. |
| IND-04 | Informativa | El escaneo `--vulnerable` SIN `--include-transitive` da falso negativo. Cualquier gate de CI que lo use reportará "limpio" erróneamente. | Evidencia §2.2. |

---

## 5. PLAN DE REMEDIACIÓN PRIORIZADO

| Prioridad | Acción | Hallazgo |
|---|---|---|
| **P0** | Forzar actualización transitiva de `System.Net.Http`/`System.Text.RegularExpressions` (o subir xunit/Test.Sdk) para eliminar los 2 CVE High | EXT-01 |
| **P1** | Alinear `Microsoft.Data.Sqlite` a rama 8.x (coherencia toolchain) | EXT-02 |
| **P1** | Eliminar `Modbus:Map` muerto de `appsettings.json` + reconsiderar `Enabled:false` por defecto | INT-01, INT-02 |
| **P1** | Actualizar `THIRD_PARTY_NOTICES.md` (versiones reales + Argon2/Serilog) | IND-01 |
| **P2** | Documentar flujo de primer arranque (seed del admin) en README | INT-03 |
| **P2** | Refuerzo doc: `SafetyCritical` es informativo, no certifica | REG-02 |
| **P2** | Limpiar árbol de trabajo (`no commit/`, archivos sueltos, specs borradas) | IND-02 |
| **P3** | Trazabilidad de `SafetyCritical` + matriz de capacidades antes de vendors | REG-03, REG-04 |

---

## 6. DECLARACIÓN DEL AUDITOR

Hallazgos basados en evidencia de disco recogida el 2026-09-12: `dotnet build/test`, `dotnet list package --vulnerable/--deprecated/--outdated --include-transitive`, lectura de fuentes y config, `git status`. Fuente de advisories: base del SDK .NET instalado + nuget.org (fuente confiable, no estimación).

**Hallazgo único que bloquea una auditoría limpia:** los 2 CVE High transitivos (pila de TEST; bajo impacto real por ser test-only, pero marcados High).

**Nota de método:** el falso negativo del escaneo sin `--include-transitive` se documenta explícitamente porque invalida cualquier reporte previo de "cero vulnerables" generado sin ese flag.

*Este informe se entregó en modo revisión; NO se modificó código de producción. NO commit, NO push. — conforme §45 del spec maestro.*