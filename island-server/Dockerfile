FROM barichello/godot-ci:mono-4.7.2

WORKDIR /app
COPY Directory.Build.props ./
COPY shared/ shared/
COPY island-server/ island-server/

WORKDIR /app/island-server
RUN godot --headless --editor --path /app/island-server --quit

ENV GODOT_HEADLESS=1
EXPOSE 7777/udp
EXPOSE 7070/tcp
ENTRYPOINT ["godot", "--headless", "--path", "/app/island-server", "--verbose"]
