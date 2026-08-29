import csv
import json
from pathlib import Path

EXCLUDED = {"50007", "50012", "50014", "50027", "55609"}
ROOT = Path(r"c:\codex\analysis")

for path in list(ROOT.rglob("*pickups_under_100*.json")) + list(ROOT.rglob("*pickups_under_100*.csv")):
    try:
        text = path.read_text(encoding="utf-8")
        if path.suffix.lower() == ".json":
            data = json.loads(text)
            if not isinstance(data, list):
                continue
            filtered = [
                row for row in data
                if not any(route in str(row.get("route_ids", "")) for route in EXCLUDED)
            ]
            path.write_text(json.dumps(filtered, ensure_ascii=False, indent=2), encoding="utf-8")
            print(f"JSON updated: {path} ({len(data)} -> {len(filtered)})")
        elif path.suffix.lower() == ".csv":
            rows = list(csv.DictReader(text.splitlines()))
            if not rows:
                continue
            filtered = [
                row for row in rows
                if not any(route in str(row.get("route_ids", "")) for route in EXCLUDED)
            ]
            with path.open("w", encoding="utf-8", newline="") as f:
                writer = csv.DictWriter(f, fieldnames=rows[0].keys())
                writer.writeheader()
                writer.writerows(filtered)
            print(f"CSV updated: {path} ({len(rows)} -> {len(filtered)})")
    except Exception:
        pass
