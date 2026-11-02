# Architecture

## Runtime boundaries

- **Client** — Godot 4.7.2 .NET, C#, Forward+, Windows-first. It owns presentation and input, never authoritative permissions or persistence.
- **Island server** — Godot 4.7.2 .NET headless process. It registers with the central API and exposes loopback health. ENet configuration is reserved but no player listener opens before temporary-entry-token validation exists.
- **Backend** — ASP.NET Core 10 modular monolith. REST endpoints are grouped by Identity, Users, Worlds, Inventory, Social, Assets, and Platform boundaries.
- **Data** — PostgreSQL is authoritative. Redis is reserved for temporary state/cache and MinIO for objects; neither substitutes for PostgreSQL business state.

## Dependencies

The client autoloads have one-way responsibilities: `AppManager` owns immutable runtime configuration; `SessionManager` owns in-memory session state; `NetworkManager` owns island transport; `AuthManager` calls the API and updates the session; `UIManager` changes scenes. Scenes depend on autoload interfaces, not on each other.

Backend modules depend on the shared data and infrastructure layers. Platform service authentication is separate from user JWT authentication. Transport contracts are versioned under `/api/v1` and `shared/protocol/v1`.

Movement-state selection lives in the engine-independent `LifeVerse.Shared.Gameplay` assembly. `PlayerController` owns physics and logical modes; `AvatarRig` owns the visible `AnimationTree`, procedural materials, and transitions. This prevents visual animation timing from becoming authoritative gameplay state.

## Configuration

Development uses `.env` plus `appsettings.Development.json`. Production uses injected secrets and `appsettings.Production.json`; repository defaults are placeholders only. Compose publishes only API port 8080 to loopback. PostgreSQL, Redis, and MinIO remain on the internal network.
