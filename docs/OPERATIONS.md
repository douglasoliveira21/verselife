# Development operations

Copy `.env.example` to `.env`, replace every placeholder, then:

```powershell
./scripts/dev-up.ps1       # build and start
./scripts/dev-logs.ps1     # follow logs
./scripts/dev-update.ps1   # pull images and rebuild
./scripts/dev-down.ps1     # stop, preserving named volumes
```

Use `docker compose -f infrastructure/compose.dev.yml ps` for status. To apply migrations outside API startup, run `./scripts/migrate.ps1` with `ConnectionStrings__Postgres` configured. Do not add `-v` to `docker compose down` unless intentionally deleting development data.

Run `dotnet test tests/LifeVerse.Api.Tests/LifeVerse.Api.Tests.csproj` for unit tests and `./tests/smoke-auth.ps1` after the API reports healthy. Start the island server only after creating an island through the API and placing its UUID plus matching service key in process environment.
