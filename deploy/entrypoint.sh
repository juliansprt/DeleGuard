#!/bin/sh
set -e
if [ "${DELEGUARD_ENTRYPOINT:-api}" = "worker" ]; then
  cd /app/worker
  exec dotnet Deleguard.Workers.dll
fi
cd /app/api
exec dotnet Deleguard.Api.dll
