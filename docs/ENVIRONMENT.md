# Environment audit

Audited on 2026-11-02 in `C:\Users\Douglas\Documents\lifeverse`.

| Item | Observed result | Status |
|---|---|---|
| OS | Microsoft Windows 10.0.26300.9818, x64 | Available |
| PowerShell | 7.6.5 | Available |
| Godot executable | Not installed system-wide; official portable Godot 4.7.2 .NET downloaded to ignored `.tools` for verification | Available locally to this checkout |
| Existing Godot metadata | Root placeholder declares Godot 4.7, non-.NET, Compatibility renderer | Migrated into dedicated projects |
| Project pin | Godot 4.7.2 stable .NET; `Godot.NET.Sdk/4.7.2` | Package restore/build pending final verification |
| .NET SDK | 10.0.301 x64; 10.0.102 also installed | Available |
| .NET runtime | Microsoft.NETCore.App and Microsoft.AspNetCore.App 10.0.9 | Available |
| Docker / Compose | `docker` executable not found | Blocked |
| Git | 2.53.0.windows.1 | Available; directory was not a repository at audit time |
| PostgreSQL tools | `psql`/`pg_isready` 18.3 | Available |
| Local PostgreSQL | `localhost:5432 - no response` | Server not running |
| Isolated PostgreSQL QA | PostgreSQL 18 cluster in ignored `.tools`, port 55432; stopped after tests | Migrations and persistence verified |
| EF migrations | Local tool manifest pins dotnet-ef 10.0.9 | Available |

## Port snapshot

TCP ports 5000, 5001, 5432, 6379, 7070, 7777, 8080, 9000, and 9001 were not listening at audit time. This is a point-in-time observation, not a reservation. The development defaults use 8080 (API), 7070 (island health), and 7777/UDP (future ENet).

## Consequences

The API and Godot C# assemblies compile with the installed .NET SDK. Client, creator, and world scenes were launched with the verified portable engine. Docker startup, PostgreSQL integration, migration application, and container persistence/restart checks still require Docker Desktop or another compatible Docker engine.
