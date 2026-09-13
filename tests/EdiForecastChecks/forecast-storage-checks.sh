#!/usr/bin/env bash
set -euo pipefail
root=$(mktemp -d)
source_dir="$root/old"
target="$root/permanent"
backup="$root/backup-one"
mkdir -p "$source_dir/edi-forecasts/sectors-st-hubert/weekly-saturday/actuals"
printf '{"forecast":1}\n' > "$source_dir/edi-forecasts/2026-09-13-v3.json"
printf '{"weekly":1}\n' > "$source_dir/edi-forecasts/sectors-st-hubert/weekly-saturday/2026-09-12-forecast.json"
printf '{"actual":1}\n' > "$source_dir/edi-forecasts/sectors-st-hubert/weekly-saturday/actuals/day.json"
bash deploy/merge-forecast-archives.sh "$target" "$backup" "$source_dir"
(cd "$target" && sha256sum --quiet -c "$backup/manifest.sha256")
cmp "$source_dir/edi-forecasts/2026-09-13-v3.json" "$target/edi-forecasts/2026-09-13-v3.json"
test -s "$backup/source-1.tar.gz"
test -f "$target/edi-forecasts/sectors-st-hubert/weekly-saturday/2026-09-12-forecast.json"
test -f "$target/edi-forecasts/sectors-st-hubert/weekly-saturday/actuals/day.json"
# A new commit must preserve the existing version and retain conflicting sources.
printf '{"forecast":2}\n' > "$source_dir/edi-forecasts/2026-09-13-v3.json"
printf '{"new":1}\n' > "$source_dir/edi-forecasts/2026-09-14-v3.json"
bash deploy/merge-forecast-archives.sh "$target" "$root/backup-two" "$target" "$source_dir"
grep -q '"forecast":1' "$target/edi-forecasts/2026-09-13-v3.json"
cmp "$source_dir/edi-forecasts/2026-09-13-v3.json" "$root/backup-two/conflicts/source-2/edi-forecasts/2026-09-13-v3.json"
test -f "$target/edi-forecasts/2026-09-14-v3.json"
test -f "$source_dir/edi-forecasts/2026-09-13-v3.json"
(cd "$target" && sha256sum --quiet -c "$root/backup-two/manifest.sha256")
echo 'EDI, delivery, actuals, repeat deployments, conflicts and checksum verification passed.'
