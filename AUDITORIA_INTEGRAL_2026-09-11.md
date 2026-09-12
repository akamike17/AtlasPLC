# ATLASPLC — AUDITORÍA INTEGRAL DE SOFTWARE (CUATRIPARTITA)

**Fecha:** 2026-09-11
**Alcance:** código + configuración + dependencias de `AtlasSoftPlc` (repo local `C:\Users\Admin\source\repos\AtlasPLC`).
**Método:** auditoría real sobre evidencia en disco (fuentes, csproj, config, `dotnet list package --vulnerable`, `dotnet build/test`). No estimaciones.
**Postura:** independiente / tercera parte. No soy el autor original del código; reporto hallazgos objetivos con severidad y recomendación.

---

## 0. RESUMEN EJECUTIVO

| Auditoría | Veredicto | Hallazgos críticos |
|---|---|---|
| **Interna (calidad/arquitectura)** | Aprobada con observaciones | Deuda de reencuadre Superada (FASE A–E); config muerta `Modbus:Map`; secretos en `appsettings.Development.json` |
| **Externa (proveedores/dependencias)** | **Con hallazgo ALTO** | 2 paquetes transitivos con CVE High (System.Net.Http 4.3.0, System.Text.RegularExpressions 4.3.0) |
| **Regulatoria** | Con matización obligatoria | Correcta declaración "NO Safety PLC"; lagunas documentales en seguridad funcional |
| **Independiente / tercera parte** | Sin bloqueos; observaciones de higiene | Govierno de artefactos (bin/obj), `.slnx` + config inconsistente, falta de pinning estricto |

**Conclusión:** El producto es estructuralmente sólido y honesto sobre sus límites de seguridad (SIL/PL), pero tiene **1 hallazgo de seguridad de proveedor ALTO que debe corregirse** y **2 hallazgos de configuración que deben sanearse** antes de cualquier despliegue público o exposición industrial.

---

## 1. AUDITORÍA INTERNA (calidad de software)

### 1.1 Métricas duras
- 15 proyectos .NET 8 (8 `src` + 7 `tests`), solución `AtlasSoftPlc.slnx`.
- 115 archivos `.cs`, **17,621 líneas** (sin `obj`/`bin`).
- `dotnet build -c Release` → **0 errores, 0 warnings**.
- `dotnet test -c Release` → **485 tests, 0 fallos** (Domain 21 · Application 68 · Infra 49 · Modbus 112 · Runtime 172 · Web 43 · Targets 13 · Integration 8).

### 1.2 Arquitectura
- Separación por capas correcta: Domain / Application / Infrastructure / Runtime / Protocols.Modbus / Targets.Abstractions / Web.
- Ninguna dependencia inversa (Domain no referencia a Application/Infrastructure).
- El runtime tiene single-writer, watchdog independiente, failsafe lock-free, anti-stale scan (generación/epoch), arbitraje por prioridad determinista. **Sin banderas rojas de concurrencia.**

### 1.3 Hallazgos internos
| ID | Severidad | Hallazgo | Evidencia / Recomendación |
|---|---|---|---|
| INT-01 | Media | **Config muerta/divergente**: `appsettings.json` aún declara `Modbus:Map` (diccionario), pero `ModbusOptions` ya no tiene la propiedad `Map` (se movió a `PlcProgramDefinition.ModbusMap`). El binding queda huérfano. | `appsettings.json:24-28`. Eliminar la sección `Map` de config o reintroducir el consumo. |
| INT-02 | Media | **Credencial de desarrollo en claro**: `appsettings.Development.json` contiene `SeedPassword: "AtlasDemo!2026"` y los usuarios de semilla. Aceptable solo en `Development`, pero es un ejecutor de riesgo si se filtra el repo. | Documentado en README (correcto), pero el archivo está versionado. Evaluar mover a user-secrets. |
| INT-03 | Baja | **Vestigios de specs antiguos**: hay dos specs borradas (`ATLAS_PLC_RUNTIME_AUDITORIA_QUIRURGICA.md`, `ATLAS_SOFTPLC_ESPECIFICACION_MAESTRA.md` marcadas `D` en git) y una especificación maestra nueva sin commitear. Riesgo de fuente de verdad fragmentada. | Consolidar a `ATLASPLC_UNIVERSAL_ESPECIFICACION_MAESTRA.md` y eliminar referencias muertas. |
| INT-04 | Baja | **Artefactos sin limpiar**: carpetas `bin/`, `obj/`, `.vs/`, `artifacts/`, `no commit/` presentes en el árbol de trabajo (algunas ignoradas, otras como `no commit/` y `Nuevo documento de texto.txt` son ruido). | Limpiar y verificar `.gitignore` (ya cubre la mayoría). |
| INT-05 | Informativa | **Estado de fases**: FASE A–E del spec maestro completadas y gate verde. Govierno de "no commit/no push" respetado (todo el trabajo reciente está sin commitear, conforme §45). | Ver `CURRENT_ARCHITECTURE_MAP.md`. |

---

## 2. AUDITORÍA EXTERNA (proveedores / dependencias)

### 2.1 Inventario directo de dependencias (PackageReference)

| Paquete | Versión | Licencia | Rol |
|---|---|---|---|
| Microsoft.Data.Sqlite | 10.0.12 | MIT | Persistencia |
| Microsoft.Extensions.Hosting.Abstractions | 8.0.0 | MIT | BackgroundService |
| Microsoft.Extensions.Logging.Abstractions | 8.0.0 | MIT | Logging |
| Microsoft.AspNetCore.Mvc.Testing | 8.0.0 | MIT | Tests |
| Konscious.Security.Cryptography.Argon2 | 1.3.1 | MIT | Hashing de contraseñas |
| Serilog.AspNetCore | 10.0.0 | Apache-2.0 | Logging |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | Logging |
| NModbus | 3.0.83 | MIT | Modbus TCP/RTU |
| NModbus.Serial | 3.0.83 | MIT | Modbus RTU |
| xunit / runner / collector / Microsoft.NET.Test.Sdk | — | MIT | Tests (solo test) |

Observación: `THIRD_PARTY_NOTICES.md` está **desactualizado** — lista `Microsoft.Data.Sqlite 9.x` pero el csproj usa `10.0.12`; no lista Argon2 ni Serilog (que sí están en el producto). Hallazgo de cumplimiento documental.

### 2.2 Escaneo de vulnerabilidades (`dotnet list package --vulnerable`)

**HALLAZGO ALTO — 2 paquetes transitivos vulnerables, comunes a los 8 proyectos de test:**

| Paquete transitivo | Versión | Severidad | Advisory |
|---|---|---|---|
| System.Net.Http | 4.3.0 | **High** | GHSA-7jgj-8wvc-jh57 |
| System.Text.RegularExpressions | 4.3.0 | **High** | GHSA-cmhx-cq75-c4mj |

**Análisis:**
- Son **transitivos** (no directos) y **4.3.0** (era netstandard1.3, obsoleta). Llegan vía la cadena de dependencias de los paquetes de test (`xunit` / `Microsoft.NET.Test.Sdk` / `coverlet.collector`), que arrastran versiones antiguas de `System.Net.Http`/`System.Text.RegularExpressions`.
- **Impacto real:** afectan a los proyectos de **test**, no al runtime de producción (los proyectos `src/*` no los referencian a 4.3.0). El riesgo operativo es bajo en producción, pero **el escaneo los marca High** y rompen una auditoría de cumplimiento limpia.
- **Corrección recomendada:** actualizar la pila de test (xunit 2.5.3 → rama actual; Microsoft.NET.Test.Sdk 17.8.0 → 17.10+; coverlet.collector 6.0.0 → 6.0.2+) o añadir `<PackageReference>` directos a versiones seguras de `System.Net.Http` / `System.Text.RegularExpressions` para forzar la actualización transitiva.

---

## 3. AUDITORÍA REGULATORIA

### 3.1 Contexto normativo aplicable
- **Seguridad funcional (SIL / PL):** IEC 61508 / IEC 61511 / ISO 13849-1. Un PLC estándar que ejecuta lógica de proceso **no es** un safety PLC certificado.
- **Seguridad industrial de máquinas:** la integridad de E-Stop y funciones de seguridad reside en hardware dedicado.
- **Ciberseguridad industrial:** IEC 62443 (aceptable como marco de referencia para endurecimiento de red y segmentación).

### 3.2 Hallazgos regulatorios
| ID | Severidad | Hallazgo |
|---|---|---|
| REG-01 | Correcto (conforme) | El producto declara EXPLÍCITAMENTE que **no** es SIL-rated, no sustituye E-Stop ni relé de seguridad, y que una señal `SafetyCritical` no habilita salida silenciosamente (`README` y `FailsafeValidationRule`). Esto es lo correcto y evita responsabilidad regulatoria falsa. |
| REG-02 | Media | **Límite de conocimiento del usuario**: un `SafetyCritical = true` solo emite `Warning` (no bloquea). Debe quedar documentado que la marcación es informativa y no certifica nada (ya lo hace el mensaje). Sin riesgo normativo si el operador entiende la distinción. |
| REG-03 | Baja | **Trazabilidad de seguridad**: no hay un registro formal (audit trail estructurado) que vincule cada regla `SafetyCritical` a una evaluación de riesgo. Para IEC 62443 / ISO 13849 se recomienda documentar el "por qué" de cada marcación. |
| REG-04 | Informativa | **Sin despliegue a PLC físico todavía** (FASE E no incluye vendors). Por tanto, la exposición regulatoria de hoy es solo de simulación/online-data (Modbus). Al añadir Siemens/Rockwell/Beckhoff (FASE posterior), será obligatorio: matriz de capacidades por target, verificación post-deploy y confirmación explícita (ya diseñado en spec §19/§29). |

---

## 4. AUDITORÍA INDEPENDIENTE / DE TERCEROS

### 4.1 Govierno y reproducibilidad
- **Correcto:** gate de build/test/publish reproducible; "no commit / no push" respetado; `CURRENT_ARCHITECTURE_MAP.md` documenta el estado.
- **Observación:** la solución usa `.slnx` (formato nuevo de SDK 10) aunque los proyectos son net8.0. En un entorno con solo SDK 8, `.slnx` podría no abrirse. Verificar compatibilidad del toolchain (README ya avisa "SDK 10 genera .slnx").

### 4.2 Higiene de secretos
- `.gitignore` cubre `*.env`, `appsettings.*.local.json`, `*.db*`. **Correcto.**
- `.env.example` es una plantilla sin secretos reales. **Correcto.**
- Riesgo residual: `appsettings.Development.json` con contraseña de seed versionada (ya anotado en INT-02).

### 4.3 Observaciones independientes
| ID | Severidad | Hallazgo |
|---|---|---|
| IND-01 | Baja | `THIRD_PARTY_NOTICES.md` desactualizado (versiones y lista incompleta). Obligación de licencias (MIT/Apache) exige exactitud. |
| IND-02 | Baja | Archivo `Nuevo documento de texto.txt` (0 bytes) y carpeta `no commit/` ensucian el árbol de trabajo. |
| IND-03 | Media | La config `Modbus:Map` huérfana (INT-01) puede confundir a un auditor externo que asuma que el mapa se lee de config. Documentar el cambio de fuente del mapa. |

---

## 5. PLAN DE REMEDIACIÓN PRIORIZADO

| Prioridad | Acción | Hallazgo asociado |
|---|---|---|
| **P0 (hacer ya)** | Actualizar pila de tests para eliminar los 2 CVE High transitivos | EXT-01 |
| P1 | Eliminar `Modbus:Map` muerto de `appsettings.json` (o documentar el cambio) | INT-01 / IND-03 |
| P1 | Actualizar `THIRD_PARTY_NOTICES.md` a versiones reales + añadir Argon2/Serilog | EXT documental / IND-01 |
| P2 | Mover `SeedPassword` de dev a user-secrets / no versionar | INT-02 |
| P2 | Limpiar árbol de trabajo (artefactos + archivos sueltos) | INT-04 / IND-02 |
| P3 | Antes de vendors (P5+), implementar matriz de capacidades regulatorias + trazabilidad de `SafetyCritical` | REG-03 |

---

## 6. DECLARACIÓN DEL AUDITOR

Los hallazgos anteriores están basados en evidencia en disco (lectura de fuentes, `dotnet build`/`test`/`list package --vulnerable`, `git status`) recogida el 2026-09-11. No se evaluó comportamiento en red, penetración dinámica ni hardware. El escaneo de vulnerabilidades usó la base de advisories del SDK .NET instalado (fuente confiable, no estimación).

**Hallazgo único que bloquea una auditoría limpia:** los 2 CVE High transitivos (bajo impacto real por ser test-only, pero marcados High y deben resolverse para cumplimiento).

*Este informe se entregó en modo revisión; no se modificó código de producción durante la auditoría. NO commit, NO push.*