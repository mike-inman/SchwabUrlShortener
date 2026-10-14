#!/bin/sh
# Run as stdin inside the disposable Compose tests image. This strips all
# Compose-provided application overrides before each real host starts.
set -eu

check_host() {
  host=$1
  project=$2
  port=$3
  log_file=$(mktemp)
  set -- \
    PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin \
    HOME=/tmp \
    NUGET_PACKAGES=/root/.nuget/packages \
    DOTNET_ENVIRONMENT=Production \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS="http://127.0.0.1:$port"
  case "$host" in
    API)
      set -- "$@" \
        AzureAd__ClientId=00000000-0000-0000-0000-000000000000 \
        'ConnectionStrings__ShortenerDb=Host=db;Port=5432;Database=Schwab_test_local;Username=shortener;Password=replace-with-a-long-random-local-password' \
        ConnectionStrings__Redis=redis:6379
      ;;
    Web)
      set -- "$@" ConnectionStrings__Redis=redis:6379
      ;;
    Analytics)
      set -- "$@" \
        ASPNETCORE_HTTP_PORTS="$port" \
        'ConnectionStrings__ShortenerDb=Host=db;Port=5432;Database=Schwab_test_local;Username=shortener;Password=replace-with-a-long-random-local-password' \
        ConnectionStrings__Redis=redis:6379 \
        Kafka__BootstrapServers=kafka:9092 \
        Kafka__Topic=managed-clicks-v2 \
        Kafka__ConsumerGroup=shortener-ci-startup
      ;;
  esac
  (
    cd "$(dirname "$project")"
    exec env -i "$@" dotnet run --project . -c Release --no-launch-profile --no-restore
  ) > "$log_file" 2>&1 &
  host_pid=$!
  ready=0
  attempt=0
  while [ "$attempt" -lt 90 ]; do
    if curl --silent --fail --output /dev/null "http://127.0.0.1:$port/health/live"; then
      ready=1
      break
    fi
    if ! kill -0 "$host_pid" 2>/dev/null; then
      break
    fi
    attempt=$((attempt + 1))
    sleep 1
  done
  kill "$host_pid" 2>/dev/null || true
  wait "$host_pid" 2>/dev/null || true
  if [ "$ready" -ne 1 ]; then
    printf '%s host failed to start from committed settings.\n' "$host" >&2
    cat "$log_file" >&2
    rm -f "$log_file"
    exit 1
  fi
  rm -f "$log_file"
  printf '%s host startup passed.\n' "$host"
}

check_host API src/Shortener.Api/Shortener.Api.csproj 18081
check_host Web src/Shortener.Web/Shortener.Web.csproj 18082
check_host Analytics src/Shortener.Analytics/Shortener.Analytics.csproj 18083
