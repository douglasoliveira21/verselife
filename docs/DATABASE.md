# Database

PostgreSQL is the source of truth. The initial EF Core migration is `InitialFoundation` under `backend/Data/Migrations`.

| Schema.table | Purpose | Retention/deletion |
|---|---|---|
| `identity.users` | Account identity and password hash | Soft delete through `deleted_at` |
| `identity.sessions` | Hashed rotating refresh sessions | Revoked rows retained for replay detection/audit; expiry indexed |
| `identity.avatars` | Initial one-to-one avatar identity | Cascades with user |
| `world.islands` | Island ownership and visibility | Soft delete through `deleted_at` |
| `platform.server_instances` | Island process lifecycle and health | Stop timestamp retained; cascades with island |

All primary keys are UUIDs and all timestamps are `timestamp with time zone` populated in UTC. Normalized username/email and active island slug have filtered unique indexes. Foreign keys restrict deleting an owner referenced by an island and cascade owned session/avatar/server lifecycle records where appropriate.

Migrations are applied by API startup with `Database.MigrateAsync`; `EnsureCreated` is not used. Production migration execution must be serialized as part of deployment before horizontal API rollout.
