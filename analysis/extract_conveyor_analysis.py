from __future__ import annotations

from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess


ROOT = Path(__file__).resolve().parents[1]
ANALYSIS_DIR = ROOT / "analysis"
ASSEMBLY = ROOT / "MySqlTool" / "bin" / "Debug" / "net10.0" / "MySqlTool.dll"
OUTPUT = ANALYSIS_DIR / "conveyor_st_hubert_results_2026-07-12.json"
RAW_OUTPUT_DIR = ANALYSIS_DIR / "conveyor_st_hubert_data"

QUERIES = {
    "timezone": "conveyor_st_hubert_timezone.sql",
    "source_types": "conveyor_discovery_source_types.sql",
    "line_summary": "conveyor_st_hubert_line_summary.sql",
    "sunday_totals": "conveyor_st_hubert_sunday_totals.sql",
    "hourly_main_lines": "conveyor_st_hubert_hourly_main_lines.sql",
    "scan_reconciliation": "conveyor_st_hubert_scan_reconciliation.sql",
    "routing_events": "conveyor_st_hubert_routing_events.sql",
    "manual_exceptions": "conveyor_st_hubert_manual_exceptions.sql",
    "top_chutes": "conveyor_st_hubert_top_chutes.sql",
    "destination_coverage": "conveyor_st_hubert_destination_coverage.sql",
    "destination_depots": "conveyor_st_hubert_destination_derived.sql",
}


def extract_query(sql_file: Path, json_file: Path) -> list[dict]:
    environment = os.environ.copy()
    environment["MYSQL_COMMAND_TIMEOUT"] = "90"
    command = [
        "dotnet",
        str(ASSEMBLY),
        "--sql-json",
        str(sql_file),
        str(json_file),
    ]
    subprocess.run(
        command,
        cwd=ROOT,
        env=environment,
        check=True,
        capture_output=True,
        text=True,
    )
    return json.loads(json_file.read_text(encoding="utf-8-sig"))


def main() -> None:
    results: dict[str, list[dict]] = {}
    RAW_OUTPUT_DIR.mkdir(exist_ok=True)
    for key, filename in QUERIES.items():
        results[key] = extract_query(
            ANALYSIS_DIR / filename,
            RAW_OUTPUT_DIR / f"{key}.json",
        )

    payload = {
        "source": {
            "database": "nationex",
            "server": "natdev02",
            "site": "ST-HUBERT",
            "depot_id": 1,
            "sorting_date": "2026-07-12",
            "operational_window": {
                "start": "2026-07-12T17:00:00-04:00",
                "end": "2026-07-13T08:00:00-04:00",
                "timezone": "America/Toronto (EDT)",
            },
            "extraction": "Agrégats opérationnels uniquement; aucune adresse, aucun code-barres et aucune ligne client exportés.",
            "extracted_at_utc": datetime.now(timezone.utc).isoformat(),
            "query_files": QUERIES,
        },
        "results": results,
    }
    OUTPUT.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    print(OUTPUT)


if __name__ == "__main__":
    main()
