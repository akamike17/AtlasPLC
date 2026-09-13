# Arquitectura de drivers e interfaz para producción

Este documento fija la referencia de diseño para preparar AtlasPLC antes de conectar un controlador físico.

## Patrones observados

| Plataforma | Interfaz de ingeniería | Canal online | Consecuencia para AtlasPLC |
|---|---|---|---|
| CODESYS | Proyecto, dispositivos, repositorio de dispositivos y visualización integrados | Depende del runtime y del dispositivo | La interfaz debe guiar selección de target, dispositivo y perfil antes de generar o conectar. |
| Siemens TIA Portal | Proyecto STEP 7 ligado a CPU, versión y firmware | S7 y OPC UA, con tags y configuración dependientes de la CPU | No existe un driver Siemens genérico seguro sin familia/modelo/firmware y capacidades declaradas. |
| Beckhoff TwinCAT | XAE/XAR, ADS routes, símbolos/TMC | ADS u OPC UA | El perfil debe separar identidad del target, ruta de conexión y símbolos publicados. |
| Schneider EcoStruxure | Proyecto y Communication Settings del controlador | OPC UA u otros canales configurados por controlador | La dirección puede ser lógica o física; el perfil debe conservar esa distinción. |
| Rockwell Studio 5000 | Logix Designer e I/O Configuration | EtherNet/IP con ruta de comunicación y módulos | La ruta incluye controlador, backplane, bridge y Ethernet; una IP aislada no describe necesariamente el target. |

## Decisión de arquitectura

AtlasPLC mantiene cuatro objetos separados:

1. `PlcProgramDefinition` / IR: lógica, variables, reglas, interlocks y estados seguros.
2. `TargetIdentity` / `TargetProfile`: fabricante, familia, modelo, firmware y capacidades.
3. `TargetConnectionProfile`: simulador o físico, transporte, NIC, dirección, puerto y parámetros seriales.
4. Adapter/protocolo: implementación concreta de lectura, escritura, generación, compilación o despliegue.

Cambiar el perfil de conexión no puede cambiar el hash semántico del programa. Un adapter no puede declarar una capacidad que no implementa, y un canal online no se considera un mecanismo de programación PLC.

## Interfaces de producción

La UI debe mostrar el flujo en este orden: seleccionar proyecto → validar IR → seleccionar target → seleccionar perfil de conexión → probar conexión → mostrar diagnóstico → habilitar la operación soportada. Las operaciones no soportadas deben aparecer como no disponibles, no como botones que terminan en éxito simulado.

Un perfil físico sin dirección no debe intentar conexión. Un perfil de simulador puede usar loopback únicamente dentro de ese perfil. La configuración base mantiene Modbus desactivado.

## Estado de drivers

Atlas Runtime tiene simulación local. Modbus implementa I/O online y no programa PLCs. No hay todavía adapters de despliegue para Siemens, Rockwell, Beckhoff, Schneider u otros proveedores. Esa ausencia es deliberada hasta fijar contratos por familia, firmware, seguridad, rutas y pruebas de integración.

## Seguridad y producción

OPC UA define autenticación, cifrado, integridad, certificados, sesiones y auditoría; por ello una futura conexión OPC UA debe exigir política de seguridad y gestión de certificados. No se debe habilitar anonymous o una conexión sin validación de endpoint como supuesto general.

`SafetyCritical` solo activa análisis y diagnósticos de ingeniería. No convierte AtlasPLC en Safety PLC ni acredita SIL o PL.

## Fuentes oficiales

- [CODESYS Development System](https://www.codesys.com/products/engineering/development-system/)
- [CODESYS Visualization](https://www.codesys.com/products/visualization/)
- [Siemens STEP 7: Communication S7-1200/S7-1500](https://docs.tia.siemens.cloud/r/en-us/v20/communication-s7-1200-s7-1500)
- [Siemens S7-1500 OPC UA server](https://docs.tia.siemens.cloud/r/en-us/v20/configuring-automation-systems/using-opc-ua-communication-s7-1200-s7-1500-s7-1500t/using-the-s7-1500-as-an-opc-ua-server-s7-1500-s7-1500t/configuring-the-opc-ua-server-s7-1500-s7-1500t/access-to-the-opc-ua-server-s7-1500-s7-1500t)
- [Beckhoff OPC UA and ADS architecture](https://infosys.beckhoff.com/content/1033/tf6100_tc3_opcua_client/15552228363.html)
- [Schneider EcoStruxure OPC UA Connection](https://product-help.schneider-electric.com/Machine%20Expert%20Twin/V2.1/en/Tw_Commu/Tw_Commu/OPCUAConn-01B87C79.html)
- [Rockwell EtherNet/IP configuration manual](https://literature.rockwellautomation.com/idc/groups/literature/documents/um/enet-um001_-es-p.pdf)
- [OPC Foundation UA Part 1: Overview and Concepts](https://reference.opcfoundation.org/specs/OPC-10000-1/4)
- [OPC Foundation UA Part 2: Security Model](https://reference.opcfoundation.org/specs/OPC-10000-2/4.5)
