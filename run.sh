#!/bin/sh
# Start the app. When a backup bucket is configured, restore the
# database if the volume is empty, then run under Litestream so
# every change is replicated.
set -e

if [ -n "$BUCKET_NAME" ]; then
    litestream restore \
        -if-db-not-exists \
        -if-replica-exists \
        /data/nudge.db
    exec litestream replicate -exec "dotnet Nudge.Web.dll"
fi

exec dotnet Nudge.Web.dll
