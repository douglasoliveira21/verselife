# Test results

Run date: 2026-11-02.

| Check | Result | Evidence / blocker |
|---|---|---|
| NuGet restore | Passed | Backend/tests restored after security upgrades; no vulnerability warning remained |
| NuGet vulnerability audit | Passed | No vulnerable direct or transitive package reported for API/tests |
| Idempotent migration SQL generation | Passed | 7,598-byte PostgreSQL script generated with all required schemas/tables |
| Backend Release build | Passed | 0 warnings, 0 errors |
| Client Godot C# Release build | Passed | Godot.NET.Sdk 4.7.2; 0 warnings, 0 errors |
| Island server C# Release build | Passed | Godot.NET.Sdk 4.7.2; 0 warnings, 0 errors |
| Unit tests | Passed | 5/5: input rules, JWT expiry claim/rejection, refresh randomness/hash |
| Docker Compose startup/health | Blocked | Docker executable absent |
| PostgreSQL connection | Blocked | `pg_isready` reported no response on localhost:5432 |
| Migration execution | Blocked | Migration generated; no reachable database |
| Register/login/invalid password/refresh/logout integration | Blocked | Smoke script created; API cannot start without PostgreSQL |
| API restart with session persistence | Blocked | Docker/PostgreSQL unavailable |
| Container volume persistence | Blocked | Docker unavailable |
| Godot client opens | Blocked | Godot executable unavailable |
| Headless server starts/restarts/registers | Blocked | Godot executable and running API unavailable |

The test runner required execution outside the filesystem sandbox because its local IPC was blocked there; the completed run reported 4 passed, 0 failed.
