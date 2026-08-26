from __future__ import annotations

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ANALYSIS_DIR = ROOT / "analysis"
RESULTS_PATH = ANALYSIS_DIR / "conveyor_st_hubert_results_2026-07-12.json"
NOTEBOOK_PATH = ANALYSIS_DIR / "conveyor_st_hubert_2026-07-12.ipynb"
REPORT_DIR = ANALYSIS_DIR / "conveyor_st_hubert_report_2026-07-12"
ARTIFACT_PATH = REPORT_DIR / "artifact.json"
REPORT_PATH = REPORT_DIR / "report.html"
OUTPUT_PATH = REPORT_DIR / "validation_results.json"


results = json.loads(RESULTS_PATH.read_text(encoding="utf-8"))
artifact = json.loads(ARTIFACT_PATH.read_text(encoding="utf-8"))
notebook = json.loads(NOTEBOOK_PATH.read_text(encoding="utf-8"))
data = results["results"]
datasets = artifact["snapshot"]["datasets"]

checks: dict[str, bool] = {}

current = data["sunday_totals"][-1]
line_total = sum(row["nb_parcel"] for row in data["line_summary"])
checks["official_total_reconciles_to_lines"] = line_total == current["PARCELS"] == 10350

raw_total = next(
    row for row in data["scan_reconciliation"] if row["LINE_ID"] == "TOTAL"
)["RAW_SCAN_ROWS"]
checks["official_vs_raw_gap_is_20"] = current["PARCELS"] - raw_total == 20
checks["official_vs_raw_gap_below_one_percent"] = (
    current["PARCELS"] - raw_total
) / current["PARCELS"] < 0.01

hour_totals: dict[str, int] = {}
for row in data["hourly_main_lines"]:
    hour_totals.setdefault(row["EVENT_HOUR"], 0)
    hour_totals[row["EVENT_HOUR"]] += row["SCAN_ROWS"]
checks["peak_hour_is_midnight_with_1999_scans"] = (
    max(hour_totals, key=hour_totals.get) == "2026-07-13 00:00:00"
    and max(hour_totals.values()) == 1999
)

routing = data["routing_events"][0]
destination_total = sum(row["UNIQUE_PARCELS"] for row in data["destination_depots"])
checks["destination_total_matches_routed_unique"] = (
    destination_total == routing["UNIQUE_ROUTED_PARCELS"] == 8340
)
checks["routing_event_math_reconciles"] = (
    routing["ROUTING_EVENTS"] - routing["UNIQUE_ROUTED_PARCELS"]
    == routing["EXTRA_ROUTING_EVENTS"]
    == 1189
)

checks["current_reject_is_five_sunday_high"] = current["REJECT_RATE_PCT"] == max(
    row["REJECT_RATE_PCT"] for row in data["sunday_totals"]
)
checks["current_noread_is_five_sunday_high"] = current["NOREAD_RATE_PCT"] == max(
    row["NOREAD_RATE_PCT"] for row in data["sunday_totals"]
)
checks["current_scale_error_is_five_sunday_high"] = current["SCALE_ERROR_RATE_PCT"] == max(
    row["SCALE_ERROR_RATE_PCT"] for row in data["sunday_totals"]
)

line_one = next(row for row in data["line_summary"] if row["line_id"] == 1)
checks["line_one_drives_most_rejections"] = line_one["nb_rejected"] / current["REJECTED"] > 0.74
checks["line_one_drives_most_noreads"] = line_one["nb_noread"] / current["NOREAD"] > 0.68

blocks = artifact["manifest"]["blocks"]
checks["report_title_is_first_block"] = (
    blocks[0]["type"] == "markdown"
    and blocks[0]["body"] == "# Convoyeur de St-Hubert — dimanche 12 juillet 2026"
)
checks["executive_summary_is_second_block"] = (
    blocks[1]["type"] == "markdown"
    and blocks[1]["body"].startswith("## Executive Summary")
)
checks["report_has_three_charts"] = len(artifact["manifest"]["charts"]) == 3
checks["report_has_two_tables"] = len(artifact["manifest"]["tables"]) == 2

source_ids = {source["id"] for source in artifact["sources"]}
source_backed_items = [
    *artifact["manifest"]["cards"],
    *artifact["manifest"]["charts"],
    *artifact["manifest"]["tables"],
]
checks["all_native_evidence_has_valid_source"] = all(
    item.get("sourceId") in source_ids for item in source_backed_items
)

checks["quality_rates_are_fractional"] = all(
    0 <= row["RATE"] <= 1 for row in datasets["quality_comparison"]
)
checks["hourly_chart_has_nine_time_points"] = len(
    {row["EVENT_HOUR_LABEL"] for row in datasets["hourly_main_lines"]}
) == 9
checks["destination_chart_has_ten_categories"] = len(datasets["top_destinations"]) == 10

code_cells = [cell for cell in notebook["cells"] if cell["cell_type"] == "code"]
checks["notebook_all_code_cells_executed"] = all(
    cell.get("execution_count") is not None for cell in code_cells
)
checks["notebook_has_no_error_outputs"] = not any(
    output.get("output_type") == "error"
    for cell in code_cells
    for output in cell.get("outputs", [])
)

checks["portable_report_exists"] = REPORT_PATH.exists() and REPORT_PATH.stat().st_size > 100_000
checks["artifact_has_no_credentials"] = not any(
    token in ARTIFACT_PATH.read_text(encoding="utf-8").lower()
    for token in ["mysql_password", "password=", "user_ro", "192.168.1.222"]
)

failed = [name for name, passed in checks.items() if not passed]
validation = {
    "assessment": "ready_to_share" if not failed else "needs_revision",
    "checks": checks,
    "failed_checks": failed,
    "builder_verification": "structural_only",
    "builder_limitation": (
        "Aucun exécutable Chromium compatible n'était disponible; le contenu, le payload, "
        "la structure et les tableaux sémantiques ont été vérifiés, mais pas le rendu navigateur."
    ),
}
OUTPUT_PATH.write_text(
    json.dumps(validation, ensure_ascii=False, indent=2),
    encoding="utf-8",
)

if failed:
    raise RuntimeError(f"Validation failed: {', '.join(failed)}")

print(f"{len(checks)} checks passed: {OUTPUT_PATH}")
