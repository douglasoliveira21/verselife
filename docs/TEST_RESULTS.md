# Test results

Run date: 2026-11-02.

## Phase 02

| Check | Result | Evidence / blocker |
|---|---|---|
| Full Release build | Passed | API, shared movement library, client, island server, tests: 0 warnings/errors |
| Automated tests | Passed | 13/13 after avatar validation and movement-state cases |
| Godot version | Passed | Portable 4.7.2 stable .NET, official build `ed1daf0bf` |
| Bootstrap/Login launch | Passed | Headless run instantiated all autoloads and Bootstrap |
| CharacterCreator scene | Passed | Direct headless launch, then four rendered frames at 1280×720 |
| Responsive creator layout | Passed | Dedicated 800×600 SubViewport capture; controls remain accessible through vertical scrolling |
| World/rig/AnimationTree scene | Passed with expected warning | Scene instantiated; isolated run lacked an authenticated session |
| Animation state runtime smoke | Passed | Godot traversed all 13 AnimationTree states without runtime errors |
| Visual placeholder inspection | Passed | Avatar and creator controls rendered without missing pieces or deformation |
| Phase 02 migration SQL | Passed | 11,644-byte idempotent script includes appearance/equipment/backfill |
| PostgreSQL save/reload integration | Passed | PostgreSQL 18 temporary cluster, both migrations, save/read, API restart, re-login, and new Godot client process reload |
| Client persistence smoke | Passed | Actual `AvatarManager` loaded `Persistent Resident` / `human_b` / `mohawk` and applied it to a fresh rig process |
| Runtime movement harness | Passed | Injected Godot actions validated walk 1.28 m, run 2.56 m, 1.42 m jump, slope ascent, wall collision, flight landing, swimming, seat alignment, and stand-up |

## Phase 01 baseline

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
| Godot client opens | Passed in Phase 02 | Portable 4.7.2 instantiated Bootstrap/Login successfully |
| Headless server starts/restarts/registers | Partially blocked | Executable now available; central API/PostgreSQL still unavailable for registration |

The test runner required execution outside the filesystem sandbox because its local IPC was blocked there; the latest completed run reported 13 passed, 0 failed.
