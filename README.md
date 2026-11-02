# LifeVerse

LifeVerse is an original persistent 3D social sandbox. This monorepository contains the Godot .NET client, authoritative island server, ASP.NET Core API, shared contracts, infrastructure, tests, and technical documentation.

## Requirements

- Godot 4.7.2 .NET (project is pinned to the supported 4.7 stable patch)
- .NET SDK 10.0.301 or a compatible 10.0 patch
- Docker Desktop with Docker Compose v2
- Git

## Development quick start

1. Copy `.env.example` to `.env` and replace every placeholder secret.
2. Run `./scripts/dev-up.ps1` from PowerShell.
3. Open `client/project.godot` with the Godot .NET editor.
4. Run the Bootstrap scene. The API defaults to `http://localhost:8080`.

Use `./scripts/dev-logs.ps1`, `./scripts/dev-down.ps1`, and `./scripts/dev-update.ps1` to inspect, stop, or rebuild the stack.

## Phase 02 controls

- W/S (or Up/Down): walk forward/back along the avatar's facing
- A/D (or Left/Right): turn the avatar in place; the camera swings behind it while moving
- Q/Z: sidestep left/right
- Double-click the ground: walk there (any movement key cancels)
- T: terrain tools (see [docs/TERRAIN.md](docs/TERRAIN.md))
- Shift: run; Space: jump/ascend; Ctrl: descend
- F: toggle flight; C: first/third person; Alt: release/capture cursor
- Mouse: camera orbit; wheel: zoom; E: interact/stand
- G: wave; H: dance

The avatar creator is available after registration and through **Edit avatar** in the world HUD.

Island persistence, permissions, terrain snapshots, official templates, and World Manager behavior are documented in [docs/ISLANDS.md](docs/ISLANDS.md).

The in-world 3D construction editor, authoritative object operations, primitive catalog, revisions, quotas, and material/physics contract are documented in [docs/BUILD_EDITOR.md](docs/BUILD_EDITOR.md).

Global inventory, validated asset uploads, versioned content, permissions, equipment, and placed instances are documented in [docs/INVENTORY_ASSETS.md](docs/INVENTORY_ASSETS.md).

Sandboxed Lua scripts and the in-world HUD editor are documented in [docs/LUA_HUDS.md](docs/LUA_HUDS.md).

Chat, friendships, groups, LiveKit room tokens, moderation and island radios are documented in [docs/SOCIAL.md](docs/SOCIAL.md).

## Phase 03 multiplayer

The World scene now contains the Multiplayer panel. Enter an island UUID to request a short-lived entry ticket, connect through ENet, see nearby authenticated avatars, and use local chat. The authoritative protocol, admission flow, rate limits, reconnect behavior, and operational settings are documented in [docs/MULTIPLAYER.md](docs/MULTIPLAYER.md).

## Repository layout

- `client`: Godot .NET desktop client
- `island-server`: authoritative Godot headless island process
- `backend`: modular ASP.NET Core API
- `shared`: versioned transport contracts and protocol notes
- `infrastructure`: containers and environment configuration
- `tests`: automated tests and smoke-test scripts

Global discovery, teleport reservations, landmarks, events, photos and minimap behavior are documented in [docs/DISCOVERY.md](docs/DISCOVERY.md).

LV Coins, the ledger, marketplace purchases and island rentals are documented in [docs/ECONOMY.md](docs/ECONOMY.md).

The administrative panel, RBAC/MFA contract, audit trail and backup runbook are documented in [docs/ADMINISTRATION.md](docs/ADMINISTRATION.md).

Windows build, launcher, installer, manifest and release gates are documented in [docs/RELEASE.md](docs/RELEASE.md).
- `docs`: product, architecture, security, operations, and progress records
