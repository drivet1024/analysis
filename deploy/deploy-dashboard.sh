#!/usr/bin/env bash
set -euo pipefail
: "${APP_DATA_PATH:?Persistent data directory is required}"
: "${GITHUB_WORKSPACE:?Runner workspace is required}"
[[ "$APP_DATA_PATH" == /opt/conveyordashboard/data ]] || { echo 'Unexpected persistent data directory'; exit 1; }
backup_dir="/opt/conveyordashboard/backups/${GITHUB_RUN_ID:?}-${GITHUB_RUN_ATTEMPT:-1}"
compose=(docker compose --project-name conveyor-dashboard)

# Complete the build before interrupting the running application.
"${compose[@]}" build
image=$("${compose[@]}" config --images)
old_volumes=()
restart_on_failure=0
restore_service() {
    result=$?
    if (( result != 0 && restart_on_failure == 1 )); then
        docker start conveyor-dashboard >/dev/null || true
    fi
    exit "$result"
}
trap restore_service EXIT
if docker container inspect conveyor-dashboard >/dev/null 2>&1; then
    old_volumes=(--volumes-from conveyor-dashboard:ro)
    restart_on_failure=1
    docker stop --time 30 conveyor-dashboard >/dev/null
fi

# Recover the active mount and both historical checkout locations, without deletion.
docker run --rm --user 0 --entrypoint /bin/sh "${old_volumes[@]}" \
    -v "$APP_DATA_PATH:/persist" \
    -v "$backup_dir:/backup" \
    -v "$GITHUB_WORKSPACE:/legacy:ro" \
    -v "$PWD/deploy:/maintenance:ro" \
    "$image" /maintenance/merge-forecast-archives.sh /persist /backup \
    /persist /app/App_Data /legacy/ConveyorDashboard/App_Data \
    /legacy/dashboard-source/ConveyorDashboard/App_Data

export DEPLOYED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
"${compose[@]}" up -d --no-build
mount=$(docker inspect conveyor-dashboard --format '{{range .Mounts}}{{if eq .Destination "/app/App_Data"}}{{.Source}}{{end}}{{end}}')
[[ "$mount" == "$APP_DATA_PATH" ]] || { echo 'Incorrect forecast storage mount'; exit 1; }
docker run --rm --entrypoint /bin/sh \
    -v "$APP_DATA_PATH:/persist:ro" -v "$backup_dir:/backup:ro" "$image" \
    -c 'cd /persist; if [ -s /backup/manifest.sha256 ]; then sha256sum --quiet -c /backup/manifest.sha256; fi'
curl --fail --silent --show-error --retry 12 --retry-delay 2 --retry-connrefused \
    --max-time 10 http://127.0.0.1:8080/api/edi/forecasts -o /dev/null
restart_on_failure=0
echo "Deployment verified. Persistent forecasts: $APP_DATA_PATH; backup: $backup_dir"
