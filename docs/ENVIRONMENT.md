# Environment audit

Audited on 2026-11-02 in `C:\Users\Douglas\Documents\lifeverse`.

| Item | Observed result | Status |
|---|---|---|
| OS | Microsoft Windows 10.0.26300.9818, x64 | Available |
| PowerShell | 7.6.5 | Available |
| Godot executable | `godot` and `godot4` absent from PATH, registry uninstall entries, and searched standard locations | Blocked for runtime launch |
| Existing Godot metadata | Root placeholder declares Godot 4.7, non-.NET, Compatibility renderer | Migrated into dedicated projects |
| Project pin | Godot 4.7.2 stable .NET; `Godot.NET.Sdk/4.7.2` | Package restore/build pending final verification |
| .NET SDK | 10.0.301 x64; 10.0.102 also installed | Available |
| .NET runtime | Microsoft.NETCore.App and Microsoft.AspNetCore.App 10.0.9 | Available |
| Docker / Compose | `docker` executable not found | Blocked |
| Git | 2.53.0.windows.1 | Available; directory was not a repository at audit time |
| PostgreSQL tools | `psql`/`pg_isready` 18.3 | Available |
| Local PostgreSQL | `localhost:5432 - no response` | Server not running |
| EF migrations | Local tool manifest pins dotnet-ef 10.0.9 | Available |

## Port snapshot

TCP ports 5000, 5001, 5432, 6379, 7070, 7777, 8080, 9000, and 9001 were not listening at audit time. This is a point-in-time observation, not a reservation. The development defaults use 8080 (API), 7070 (island health), and 7777/UDP (future ENet).

## Consequences

The API and Godot C# assemblies can be compiled with the installed .NET SDK. Docker startup, PostgreSQL integration, migration application, container persistence/restart checks, and actual Godot editor/headless launches cannot be truthfully certified until Docker Desktop and Godot 4.7.2 .NET are installed or made available on PATH.
