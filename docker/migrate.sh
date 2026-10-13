#!/bin/sh
set -eu

if [ -z "${ConnectionStrings__ShortenerDb:-}" ]; then
    echo 'Migration blocked: PostgreSQL connection is not configured.' >&2
    exit 2
fi

database_name=$(printf '%s' "$ConnectionStrings__ShortenerDb" | tr ';' '\n' | awk -F= '
    tolower($1) ~ /^[[:space:]]*(database|initial catalog)[[:space:]]*$/ {
        value=$2; gsub(/^[[:space:]]+|[[:space:]]+$/, "", value); print value; exit
    }')
if [ "$database_name" != 'Schwab_test_local' ] && [ "${SCHWAB_APP_MIGRATION_APPROVED:-}" != 'yes' ]; then
    echo 'Migration blocked: only Schwab_test_local is allowed without an explicit application-database approval flag.' >&2
    exit 2
fi
if [ -z "$database_name" ]; then
    echo 'Migration blocked: database name is missing.' >&2
    exit 2
fi

exec dotnet ef database update \
    --project src/Shortener.Infrastructure/Shortener.Infrastructure.csproj \
    --startup-project src/Shortener.Infrastructure/Shortener.Infrastructure.csproj \
    --context ShortenerDbContext --configuration Release --no-build
