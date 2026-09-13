# Paquete instalable AtlasPLC

Ejecuta `Build-Installer.ps1` desde la raíz del repositorio para generar un paquete reproducible `artifacts/installer/AtlasPLC-Workbench-win-x64.zip`.

El paquete usa el runtime instalado de .NET 8 (`--self-contained false`), conserva los archivos publicados y contiene `INSTALAR.txt`. Los datos y logs se guardan fuera de la aplicación en `%LocalAppData%\AtlasSoftPlc`.

Antes de producción cambia `Auth:SeedPassword`, configura `AllowedHosts` y usa HTTPS detrás de un proxy o certificado válido.
