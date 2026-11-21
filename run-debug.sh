#!/usr/bin/env bash
# Starts the backend (Debug build, JIT) and the web interface (Vite dev server with hot reload).
# Press Ctrl+C to stop both.
#
#   backend  http://localhost:5174   (the API)
#   web      http://localhost:5173   (open this one; it sends /api to the backend)
#
# Extra arguments go to the backend, for example: ./run-debug.sh --DataDir=/tmp/backup-data
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$root"

pids=()

stop() {
  trap - INT TERM EXIT
  echo
  echo "Stopping..."
  for pid in "${pids[@]}"; do
    # Each child runs in its own process group, so the whole tree is stopped.
    kill -TERM -- "-$pid" 2>/dev/null || true
  done
  for pid in "${pids[@]}"; do
    wait "$pid" 2>/dev/null || true
  done
  exit 0
}
trap stop INT TERM EXIT

if [ ! -d src/web/node_modules ]; then
  echo "Installing the web dependencies..."
  (cd src/web && pnpm install)
fi

echo "Starting the backend on http://localhost:5174 ..."
setsid dotnet run --project src/Backup.Server --configuration Debug --no-launch-profile -- --Port=5174 "$@" &
pids+=("$!")

echo "Starting the web interface on http://localhost:5173 ..."
(cd src/web && exec setsid pnpm exec vite --host localhost --port 5173 --strictPort) &
pids+=("$!")

# Ends as soon as either one stops, or on Ctrl+C.
wait -n "${pids[@]}" || true
