# Avisos de terceros

| Paquete | Versión | Repositorio | Licencia | Propósito |
|---------|---------|-------------|----------|-----------|
| Microsoft.Data.Sqlite | 10.0.12 | https://github.com/dotnet/efcore | MIT | Persistencia SQLite |
| Microsoft.Extensions.Hosting.Abstractions | 8.0.0 | https://github.com/dotnet/runtime | MIT | BackgroundService |
| Microsoft.Extensions.Logging.Abstractions | 8.0.0 | https://github.com/dotnet/runtime | MIT | Logging |
| NModbus | 3.0.83 | https://github.com/NModbus/NModbus | MIT | Modbus TCP/RTU |
| NModbus.Serial | 3.0.83 | https://github.com/NModbus/NModbus | MIT | Transporte serial Modbus |
| Konscious.Security.Cryptography.Argon2 | 1.3.1 | https://github.com/kmaragon/Konscious.Security.Cryptography | MIT | Hashing Argon2id de contraseñas |
| Serilog.AspNetCore | 10.0.0 | https://github.com/serilog/serilog-aspnetcore | Apache-2.0 | Logging estructurado |
| Serilog.Sinks.File | 7.0.0 | https://github.com/serilog/serilog-sinks-file | Apache-2.0 | Log rotativo a archivo |

Las dependencias exclusivas de pruebas se mantienen en los proyectos de tests y no forman parte del runtime publicado: xunit 2.9.3, xunit.runner.visualstudio 4.0.0,
Microsoft.NET.Test.Sdk 18.10.0, coverlet.collector 10.0.1 y
Microsoft.AspNetCore.Mvc.Testing 8.0.0.

Las versiones finales quedan fijadas en cada `.csproj`. Para OPC UA (OPC
Foundation) y otros protocolos de fases posteriores, revisar y conservar sus
avisos/licencias aplicables al incorporarlos.
