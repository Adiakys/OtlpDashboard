#!/usr/bin/env bash
# Usage: tests/OpenTelemetryDashboard.LoadTest/run.sh [sqlite|postgres] [load test options...]
# Publishes the host in Release, starts it on ports 14317/14318 against a
# fresh database, runs the load test against it and tears everything down.
set -euo pipefail

provider="${1:-sqlite}"
shift || true

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$here/.out"
host_dir="$out/host"
data_dir="$(mktemp -d)"
pg_container="oteldash-bench-pg"
host_pid=""

cleanup() {
  [[ -n "$host_pid" ]] && kill "$host_pid" 2>/dev/null && wait "$host_pid" 2>/dev/null || true
  [[ "$provider" == "postgres" ]] && docker rm -f "$pg_container" >/dev/null 2>&1 || true
  rm -rf "$data_dir"
}
trap cleanup EXIT

echo "Publishing host..."
dotnet publish "$root/src/OpenTelemetryDashboard.Host" -c Release -o "$host_dir" --nologo -v quiet >/dev/null
dotnet build "$here" -c Release --nologo -v quiet >/dev/null

export ASPNETCORE_ENVIRONMENT=Production
export Logging__LogLevel__Default=Warning
export Dashboard__Ingestion__Grpc__Port=14317
export Dashboard__Ingestion__Http__Port=14318
export Dashboard__RateLimits__OtlpIngest__PermitsPerSecond=1000000
export Dashboard__RateLimits__OtlpIngest__Burst=100000
export Dashboard__RateLimits__ReadApi__PermitsPerSecond=1000000
export Dashboard__RateLimits__ReadApi__Burst=100000

case "$provider" in
  sqlite)
    export Dashboard__Storage__Provider=Sqlite
    export ConnectionStrings__Sqlite="Data Source=$data_dir/telemetry.db"
    ;;
  postgres)
    docker rm -f "$pg_container" >/dev/null 2>&1 || true
    docker run -d --name "$pg_container" -p 55432:5432 -e POSTGRES_USER=otel -e POSTGRES_PASSWORD=otel -e POSTGRES_DB=oteldash postgres:16-alpine >/dev/null
    until docker exec "$pg_container" pg_isready -U otel -d oteldash >/dev/null 2>&1; do sleep 0.5; done
    sleep 1
    export Dashboard__Storage__Provider=PostgreSql
    export ConnectionStrings__PostgreSql="Host=localhost;Port=55432;Username=otel;Password=otel;Database=oteldash"
    ;;
  *)
    echo "Unknown provider '$provider' (expected sqlite or postgres)" >&2
    exit 1
    ;;
esac

(cd "$host_dir" && exec dotnet OpenTelemetryDashboard.Host.dll >"$out/host.log" 2>&1) &
host_pid=$!

dotnet "$here/bin/Release/net10.0/OpenTelemetryDashboard.LoadTest.dll" \
  --grpc http://localhost:14317 --http http://localhost:14318 \
  --pid "$host_pid" --label "$provider" --out "$here/results" "$@"

case "$provider" in
  sqlite)
    echo "Database size: $(du -ch "$data_dir"/telemetry.db* | tail -1 | cut -f1)"
    ;;
  postgres)
    echo "Database size: $(docker exec "$pg_container" psql -U otel -d oteldash -tAc "select pg_size_pretty(pg_database_size('oteldash'))")"
    ;;
esac
