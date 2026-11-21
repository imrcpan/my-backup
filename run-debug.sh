#!/usr/bin/env bash
# Starts the backend (Debug build, JIT), which also serves the web interface, on one port:
#
#   http://localhost:1580
#
# The interface is built first and then rebuilt whenever a file of src/web changes; reload
# the page to see a change. Press Ctrl+C to stop everything.
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

echo "Building the web interface..."
(cd src/web && pnpm exec vite build)

echo "Watching the web interface for changes..."
(cd src/web && exec setsid pnpm exec vite build --watch) &
pids+=("$!")

echo "Starting the backend on http://localhost:1580 ..."
setsid dotnet run --project src/Backup.Server --configuration Debug --no-launch-profile -- "$@" &
pids+=("$!")

# Ends as soon as either one stops, or on Ctrl+C.
wait -n "${pids[@]}" || true
