FROM barichello/godot-ci:mono-4.7.2

USER root
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 ca-certificates && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY Directory.Build.props ./
COPY shared/ shared/
COPY scripting/ scripting/
COPY island-server/ island-server/

RUN dotnet restore /app/island-server/LifeVerse.IslandServer.csproj
RUN dotnet build /app/island-server/LifeVerse.IslandServer.csproj -c Release --no-restore
RUN mkdir -p /app/island-server/.godot/mono/temp/bin/Debug \
    && dotnet build /app/island-server/LifeVerse.IslandServer.csproj -c Debug --no-restore -o /app/island-server/.godot/mono/temp/bin/Debug \
    && test -f /app/island-server/.godot/mono/temp/bin/Debug/LifeVerse.IslandServer.dll

WORKDIR /app/island-server
RUN godot --headless --editor --path /app/island-server --build-solutions --quit --verbose \
    && test -f /app/island-server/.godot/mono/temp/bin/Debug/LifeVerse.IslandServer.dll

ENV GODOT_HEADLESS=1
EXPOSE 7777/udp
EXPOSE 7070/tcp
ENTRYPOINT ["godot", "--headless", "--path", "/app/island-server", "--verbose"]
