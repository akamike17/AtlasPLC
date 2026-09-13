# Evidencia de conectores de laboratorio

Fecha de ejecución: 2026-09-12

## Siemens S7 — servidor Snap7

- Servidor: `python-snap7` `3.1.2`, proceso `snap7_server.py`.
- Endpoint: `127.0.0.1:1102` (puerto no privilegiado para laboratorio).
- Prueba: `snap7_probe.py`.
- Resultado: **PASS**.
- Evidencia: conexión establecida; DB1 bytes 0..3 leídos como `[0, 0, 0, 0]`, escritos como `[65, 84, 76, 83]` (`ATLS`) y leídos nuevamente con el mismo valor; `roundtrip: True`.

Esto prueba el protocolo S7 contra un servidor real de laboratorio. El adaptador de despliegue S7 de AtlasPLC aún debe conectarse a este endpoint antes de afirmar que una rutina fue enviada desde la vista.

## OpenPLC

- Runtime oficial v4 compilado en WSL2 `AtlasUbuntu`.
- Endpoint detectado: `127.0.0.1:8443`.
- Estado: **PASS de disponibilidad**, pendiente de adaptador de despliegue AtlasPLC.

## Rockwell Logix — EtherNet/IP CIP

- Servidor: `cpppo` `5.2.5`, ejecutado como servidor EtherNet/IP de laboratorio.
- Endpoint: `127.0.0.1:44818`.
- Prueba real: lectura de `Atlas_Start` (`False`), escritura CIP `BOOL=True` y lectura de retorno (`True`).
- Resultado del round-trip: **PASS**.
- Repetición: 10 lecturas CIP independientes, `rockwell_cycles=10/10`.
- El probe del target `rockwell-logix` verifica ahora el endpoint TCP desde la vista Targets.

## Beckhoff TwinCAT — ADS

- Servidor: SoftBeckhoff, compilado desde su fuente y ejecutado con router ADS interno.
- Endpoint: `127.0.0.1:48898`, AMS target `192.168.1.20.1.1:852`.
- Prueba: cliente `AdsProbe` usando `ReadWriteAsync`/`WriteAsync` sobre el grupo de memoria ADS del proceso.
- Resultado: **PASS**; `connected=True`, `before=00`, `after=42`, `roundtrip=True`.
- Repetición: 10 ciclos ADS independientes, `beckhoff_cycles=10/10`.
- Nota: la ruta simbólica del paquete preview presenta una incompatibilidad; la ruta de memoria ADS directa sí completó lectura/escritura real.

## Omron FINS — UDP

- Servidor: emulador FINS incluido en `gofins`, ejecutado en loopback.
- Endpoint: PLC `127.0.0.1:9601`, cliente `127.0.0.1:9600`.
- Prueba real: lectura DM, escritura de palabras `[1, 65535]`, lectura de retorno, escritura/lectura de `Float64=15.6` y cadena `teststring`.
- Resultado: **PASS**; los valores retornaron correctamente por FINS/UDP.

## Mitsubishi MELSEC — SLMP/MC 3E

- Servidor: `SLMP.MockServer`, corregido para ensamblar tramas TCP fragmentadas.
- Endpoint: `127.0.0.1:2000`.
- Prueba real: cliente SLMP ejecutó self-test, 100 escrituras secuenciales D0-D99, lectura por bloque con integridad, cadenas, estructura y patrón de bits M100-M163.
- Resultado: **PASS**; el ejemplo reportó `Self Test: PASS`, `Data integrity check: PASS`, `String operations: PASS`, `Struct roundtrip test: PASS` y `Bit pattern roundtrip: PASS`.

## Modbus TCP y simulación Atlas

Ya cuentan con pruebas automatizadas existentes en el repositorio. Se mantienen como conectores implementados y no se repiten como si fueran nuevos.

## Ejecución desde la vista AtlasPLC

La vista autenticada `/Lab` ejecutó la batería persistida de los 10 programas contra cada endpoint y mostró el resultado en pantalla:

- Siemens S7: `10/10 casos PASS`.
- Rockwell EtherNet/IP: `10/10 casos PASS`.
- Mitsubishi SLMP/MC 3E: `10/10 casos PASS`.
- Omron FINS/UDP: `10/10 casos PASS`.
- Beckhoff ADS: `10/10 casos PASS`.

La matriz sigue distinguiendo probe, I/O de laboratorio y despliegue de proyecto a un PLC físico: estas pruebas validan lectura/escritura de los protocolos y sus simuladores, no una descarga propietaria a Studio/TIA/TwinCAT.
