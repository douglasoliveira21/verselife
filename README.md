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

- WASD: move relative to camera
- Shift: run; Space: jump/ascend; Ctrl: descend
- F: toggle flight; C: first/third person; Alt: release/capture cursor
- Mouse: camera orbit; wheel: zoom; E: interact/stand
- G: wave; H: dance

The avatar creator is available after registration and through **Edit avatar** in the world HUD.

## Repository layout

- `client`: Godot .NET desktop client
- `island-server`: authoritative Godot headless island process
- `backend`: modular ASP.NET Core API
- `shared`: versioned transport contracts and protocol notes
- `infrastructure`: containers and environment configuration
- `tests`: automated tests and smoke-test scripts
- `docs`: product, architecture, security, operations, and progress records
