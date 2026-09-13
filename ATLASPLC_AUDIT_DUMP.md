# AtlasPLC — volcado de auditoría para revisión

## Objetivo

Este commit reúne la evolución del Workbench hacia un flujo seguro de diseño, simulación, revisión y preparación de despliegue PLC.

## Cambios auditables

- Catálogo persistente de programas, plantillas, duplicación, carga, guardado, importación y exportación.
- Simulación local determinista con entradas, salidas, temporizadores, failsafe y timeline.
- Editor gráfico con paleta de componentes, lienzo, selección, movimiento, propiedades, conexiones visibles, zoom, deshacer/rehacer y guardado del diseño.
- Aplicación del gráfico al programa activo y ejecución en el runtime local en estado seguro.
- Revisión previa con advertencias y bloqueo de despliegue físico hasta configurar/verificar el target.
- Configuración persistente de targets en SQLite mediante migración de esquema v5.
- Roles/autenticación de la aplicación y separación de permisos administrativos.
- Emisores Structured Text y PLCopen XML, documentación operativa y paquete de instalación.

## Validación ejecutada

- `dotnet build AtlasSoftPlc.Web.csproj --no-restore -c Debug` — correcto, 0 errores.
- Se levantó la aplicación localmente en `http://localhost:5193`.
- Se verificaron las rutas de login, simulación, Targets y el editor gráfico en navegador.
- El runtime aplica failsafe al reemplazar o instalar un programa.

## Límites que deben comprobarse en revisión

- Los PLC físicos requieren sus servidores/adaptadores y no deben declararse conectados sin una prueba real.
- La simulación local no sustituye una validación de seguridad física certificada.
- Las advertencias de restauración, exportación y despliegue deben revisarse con los casos de prueba del entorno receptor.

## Archivos deliberadamente excluidos

No se incluyen logs de ejecución, carpetas binarias/publicadas, bases de datos locales ni documentos temporales generados durante las pruebas.
