#!/bin/sh
# Run with the application stopped. Sources remain read-only and are never removed.
set -eu
target=$1
backup=$2
shift 2
mkdir -p "$target/edi-forecasts" "$backup"
source_number=0
for source in "$@"; do
    source_number=$((source_number + 1))
    [ -d "$source/edi-forecasts" ] || continue
    tar -czf "$backup/source-$source_number.tar.gz" -C "$source" edi-forecasts
    find "$source/edi-forecasts" -type f -name '*.json' -exec sh -eu -c '
        source=$1; target=$2; backup=$3; source_number=$4
        shift 4
        for file do
            relative=${file#"$source/"}
            destination="$target/$relative"
            if [ -e "$destination" ]; then
                if ! cmp -s "$file" "$destination"; then
                    conflict="$backup/conflicts/source-$source_number/$relative"
                    mkdir -p "$(dirname "$conflict")"
                    cp -p "$file" "$conflict"
                    cmp -s "$file" "$conflict"
                    echo "Archive conflict preserved in backup: $relative"
                fi
            else
                mkdir -p "$(dirname "$destination")"
                cp -p "$file" "$destination"
                cmp -s "$file" "$destination"
            fi
        done
    ' sh "$source" "$target" "$backup" "$source_number" {} +
done
# Record all files that must survive container replacement, including actuals.
cd "$target"
find edi-forecasts -type f -name '*.json' -exec sha256sum {} + | sort > "$backup/manifest.sha256"
echo "Forecast archives preserved: $(wc -l < "$backup/manifest.sha256")"
