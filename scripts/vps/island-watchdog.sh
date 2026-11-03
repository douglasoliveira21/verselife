#!/usr/bin/env bash
# Keeps island-server Swarm services on "stop-first" updates.
#
# The island server publishes host-mode ports (UDP game port, TCP health port). With the default "start-first"
# order the replacement container can never start because the old one still holds the ports, so every deploy
# from the panel leaves a task Pending. The panel recreates the service settings on each deploy, so this script
# re-applies stop-first whenever it finds anything else. Run it periodically (see install-island-watchdog.sh).
#
# Usage: island-watchdog.sh [service ...]      (default service: verselife_island)
set -euo pipefail

services=("$@")
[ ${#services[@]} -eq 0 ] && services=(verselife_island)

for service in "${services[@]}"; do
  order=$(docker service inspect "$service" --format '{{.Spec.UpdateConfig.Order}}' 2>/dev/null || true)
  if [ -z "$order" ]; then
    echo "$(date -Is) $service: service not found, skipping"
    continue
  fi
  if [ "$order" != "stop-first" ]; then
    echo "$(date -Is) $service: update order is '$order', setting stop-first"
    docker service update --update-order stop-first --update-parallelism 1 --detach=true "$service" > /dev/null
  fi
done
