#!/usr/bin/env bash
# Usage: tests/OpenTelemetryDashboard.LoadTest/run.sh [sqlite|postgres] [load test options...]
# Publishes the host in Release, starts it on ports 14317/14318 against a
# fresh database, runs the load test against it and tears everything down.
# DATA_DIR keeps the database there (SQLite file or PostgreSQL data directory)
# instead of a temp dir, to rerun queries against the same data with --skip-ingest.
# CPUS and MEMORY (docker --cpus / --memory values) run the host in a container
# with those limits, like a small deployment.
set -euo pipefail

provider="${1:-sqlite}"
shift || true

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$here/.out"
host_dir="$out/host"
mkdir -p "$out"
data_dir="${DATA_DIR:-$(mktemp -d -p "$out" data.XXXXXX)}"
mkdir -p "$data_dir"
pg_container="oteldash-bench-pg"
host_container="oteldash-bench-host"
host_pid=""

cleanup() {
  if [[ -n "${CPUS:-}${MEMORY:-}" ]]; then
    docker logs "$host_container" >"$out/host.log" 2>&1 || true
    docker rm -f "$host_container" >/dev/null 2>&1 || true
  else
    [[ -n "$host_pid" ]] && kill "$host_pid" 2>/dev/null && wait "$host_pid" 2>/dev/null || true
  fi
  [[ "$provider" == "postgres" ]] && docker rm -f "$pg_container" >/dev/null 2>&1 || true
  [[ -z "${DATA_DIR:-}" ]] && rm -rf "$data_dir" || true
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
    pg_volume=()
    [[ -n "${DATA_DIR:-}" ]] && pg_volume=(-v "$data_dir/pg:/var/lib/postgresql/data")
    docker run -d --name "$pg_container" -p 55432:5432 "${pg_volume[@]}" -e POSTGRES_USER=otel -e POSTGRES_PASSWORD=otel -e POSTGRES_DB=oteldash postgres:16-alpine >/dev/null
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

if [[ -n "${CPUS:-}${MEMORY:-}" ]]; then
  limits=()
  [[ -n "${CPUS:-}" ]] && limits+=(--cpus "$CPUS")
  [[ -n "${MEMORY:-}" ]] && limits+=(--memory "$MEMORY" --memory-swap "$MEMORY")
  env_args=()
  while IFS='=' read -r name value; do env_args+=(-e "$name=$value"); done \
    < <(env | grep -E '^(ASPNETCORE_|Logging__|Dashboard__|ConnectionStrings__)')
  # The container user may be remapped (userns), so it needs write access by mode.
  chmod -R a+rwX "$data_dir"
  docker rm -f "$host_container" >/dev/null 2>&1 || true
  docker run -d --name "$host_container" --network host --user "$(id -u):$(id -g)" -e HOME=/tmp \
    "${limits[@]}" "${env_args[@]}" -v "$host_dir:$host_dir" -v "$data_dir:$data_dir" -w "$host_dir" \
    mcr.microsoft.com/dotnet/aspnet:10.0 dotnet OpenTelemetryDashboard.Host.dll >/dev/null
  host_pid="$(docker inspect -f '{{.State.Pid}}' "$host_container")"
else
  (cd "$host_dir" && exec dotnet OpenTelemetryDashboard.Host.dll >"$out/host.log" 2>&1) &
  host_pid=$!
fi

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
