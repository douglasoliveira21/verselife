# LifeVerse

LifeVerse is an original persistent 3D social sandbox. This monorepository contains the Unity client, the island server (Unity dedicated server) and its supervisor, the ASP.NET Core API, shared contracts, infrastructure, tests, and technical documentation.

## Requirements

- Unity 6000.6.9f1 (HDRP), with the "Linux Dedicated Server Build Support" module to build the island server
- .NET SDK 10.0.301 or a compatible 10.0 patch
- Docker Desktop with Docker Compose v2
- Git

## Development quick start

1. Copy `.env.example` to `.env` and replace every placeholder secret.
2. Run `./scripts/dev-up.ps1` from PowerShell.
3. Build the libraries the Unity project uses: `powershell -File unity\UnityPlugins\build-plugins.ps1`.
4. Set `LIFEVERSE_API_URL=http://localhost:8080` (without it the client talks to the production API), open `unity/UnityClient` in Unity and press Play.

Commands for tests and builds are in [unity/README.md](unity/README.md); what is still open is in [docs/unity-migration/FALTA_FAZER.md](docs/unity-migration/FALTA_FAZER.md).

Use `./scripts/dev-logs.ps1`, `./scripts/dev-down.ps1`, and `./scripts/dev-update.ps1` to inspect, stop, or rebuild the stack.

## Documentation written for the former Godot client

The game was first written in Godot; those projects (`client/`, `island-server/`) were removed on 2026-11-27 and remain in the git history (last commit with them: `177c0c1`). Many documents in `docs/` were written then: the API, data and product rules they describe still hold, but file paths, controls and test scenes of the Godot projects do not.

Island persistence, permissions, terrain snapshots, official templates, and World Manager behavior are documented in [docs/ISLANDS.md](docs/ISLANDS.md).

The in-world 3D construction editor, authoritative object operations, primitive catalog, revisions, quotas, and material/physics contract are documented in [docs/BUILD_EDITOR.md](docs/BUILD_EDITOR.md).

Global inventory, validated asset uploads, versioned content, permissions, equipment, and placed instances are documented in [docs/INVENTORY_ASSETS.md](docs/INVENTORY_ASSETS.md).

Sandboxed Lua scripts and the in-world HUD editor are documented in [docs/LUA_HUDS.md](docs/LUA_HUDS.md).

Chat, friendships, groups, LiveKit room tokens, moderation and island radios are documented in [docs/SOCIAL.md](docs/SOCIAL.md).

## Multiplayer

The client asks the API for a short-lived entry ticket and connects to the island server over LifeVerse Wire 12 ([docs/unity-migration/WIRE_PROTOCOL.md](docs/unity-migration/WIRE_PROTOCOL.md)). Island servers start on demand ([docs/ISLAND_HOSTING.md](docs/ISLAND_HOSTING.md)).

## Repository layout

- `unity`: Unity client, engine-free island server core and network protocol
- `island-supervisor`: starts one island server per island players ask to enter
- `backend`: modular ASP.NET Core API
- `shared`: versioned game rules and transport contracts
- `scripting`: sandboxed Lua runtime
- `assets`: files that ship with the game (body, sounds, icon, licences)
- `infrastructure`: containers and environment configuration
- `tests`: automated tests and smoke-test scripts

Global discovery, teleport reservations, landmarks, events, photos and minimap behavior are documented in [docs/DISCOVERY.md](docs/DISCOVERY.md).

LV Coins, the ledger, marketplace purchases and island rentals are documented in [docs/ECONOMY.md](docs/ECONOMY.md).

The administrative panel, RBAC/MFA contract, audit trail and backup runbook are documented in [docs/ADMINISTRATION.md](docs/ADMINISTRATION.md).

Lua scripts on objects (run on the island server) are documented in [docs/SCRIPTING.md](docs/SCRIPTING.md).

Windows build, launcher, installer, manifest and release gates are documented in [docs/RELEASE.md](docs/RELEASE.md).
- `docs`: product, architecture, security, operations, and progress records
