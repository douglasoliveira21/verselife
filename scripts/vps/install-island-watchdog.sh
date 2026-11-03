#!/usr/bin/env bash
# Installs island-watchdog.sh as a systemd timer that runs every 30 seconds. Run as root on the Docker host.
#
# Usage: sudo ./install-island-watchdog.sh [service ...]      (default service: verselife_island)
# Remove: systemctl disable --now lifeverse-island-watchdog.timer && rm /etc/systemd/system/lifeverse-island-watchdog.* /usr/local/bin/lifeverse-island-watchdog
set -euo pipefail

[ "$(id -u)" -eq 0 ] || { echo "Run as root." >&2; exit 1; }
command -v docker > /dev/null || { echo "Docker is not installed." >&2; exit 1; }
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
install -m 0755 "$here/island-watchdog.sh" /usr/local/bin/lifeverse-island-watchdog
services="${*:-verselife_island}"

cat > /etc/systemd/system/lifeverse-island-watchdog.service <<UNIT
[Unit]
Description=Keep LifeVerse island services on stop-first updates
After=docker.service
Requires=docker.service

[Service]
Type=oneshot
ExecStart=/usr/local/bin/lifeverse-island-watchdog $services
UNIT

cat > /etc/systemd/system/lifeverse-island-watchdog.timer <<'UNIT'
[Unit]
Description=Run the LifeVerse island watchdog every 30 seconds

[Timer]
OnBootSec=30
OnUnitActiveSec=30
AccuracySec=5

[Install]
WantedBy=timers.target
UNIT

systemctl daemon-reload
systemctl enable --now lifeverse-island-watchdog.timer
systemctl start lifeverse-island-watchdog.service
echo "Installed. Check with: systemctl list-timers lifeverse-island-watchdog.timer ; journalctl -u lifeverse-island-watchdog -n 20"
