# Island server image that starts islands on demand: a small supervisor process asks the API which islands players
# are waiting for and launches one headless Godot island server per island (one UDP port each, idle ones exit).
# Build from the repository root:  docker build -f island-supervisor/Dockerfile -t lifeverse-islands .
FROM barichello/godot-ci:mono-4.7.2

USER root
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 ca-certificates && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY Directory.Build.props ./
COPY shared/ shared/
COPY scripting/ scripting/
COPY island-server/ island-server/
COPY island-supervisor/ island-supervisor/

RUN dotnet restore /app/island-server/LifeVerse.IslandServer.csproj
RUN dotnet build /app/island-server/LifeVerse.IslandServer.csproj -c Release --no-restore
RUN mkdir -p /app/island-server/.godot/mono/temp/bin/Debug \
    && dotnet build /app/island-server/LifeVerse.IslandServer.csproj -c Debug --no-restore -o /app/island-server/.godot/mono/temp/bin/Debug \
    && test -f /app/island-server/.godot/mono/temp/bin/Debug/LifeVerse.IslandServer.dll

WORKDIR /app/island-server
RUN godot --headless --editor --path /app/island-server --build-solutions --quit --verbose \
    && test -f /app/island-server/.godot/mono/temp/bin/Debug/LifeVerse.IslandServer.dll

RUN dotnet publish /app/island-supervisor/LifeVerse.IslandSupervisor.csproj -c Release -o /opt/supervisor

ENV GODOT_HEADLESS=1 \
    SUPERVISOR_GODOT_PATH=godot \
    SUPERVISOR_PROJECT_PATH=/app/island-server \
    SUPERVISOR_PORT_START=7777 \
    SUPERVISOR_MAX_ISLANDS=8 \
    SUPERVISOR_IDLE_MINUTES=10
# One UDP port per island that can run at once (SUPERVISOR_PORT_START + SUPERVISOR_MAX_ISLANDS - 1).
EXPOSE 7777-7784/udp
WORKDIR /opt/supervisor
ENTRYPOINT ["dotnet", "/opt/supervisor/LifeVerse.IslandSupervisor.dll"]
