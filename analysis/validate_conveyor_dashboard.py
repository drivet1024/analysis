from __future__ import annotations

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ANALYSIS_DIR = ROOT / "analysis"
OUTPUT_DIR = ANALYSIS_DIR / "conveyor_dashboard_st_hubert"
ARTIFACT_PATH = OUTPUT_DIR / "artifact.json"
HTML_PATH = OUTPUT_DIR / "dashboard.html"
RESULTS_PATH = OUTPUT_DIR / "validation_results.json"

artifact = json.loads(ARTIFACT_PATH.read_text(encoding="utf-8"))
datasets = artifact["snapshot"]["datasets"]
summary_rows = datasets["summary"]
default_date = "2026-07-12"
summary = {
    row["CONVEYOR_GROUP"]: row
    for row in summary_rows
    if row["SORT_DATE"] == default_date
}
checks = []


def check(name: str, condition: bool, detail: str) -> None:
    checks.append({"name": name, "status": "pass" if condition else "fail", "detail": detail})


check("groupes attendus", set(summary) == {"Haut", "Sol"}, f"groupes={sorted(summary)}")
available_dates = sorted({row["SORT_DATE"] for row in summary_rows})
check("dates préchargées", len(available_dates) == 38, f"dates={len(available_dates)}")
check(
    "total officiel",
    sum(row["OFFICIAL_PASSAGES"] for row in summary.values()) == 10350,
    f"total={sum(row['OFFICIAL_PASSAGES'] for row in summary.values())}",
)
check(
    "total brut",
    sum(row["RAW_SCAN_ROWS"] for row in summary.values()) == 10330,
    f"total={sum(row['RAW_SCAN_ROWS'] for row in summary.values())}",
)

recirculation = {row["CONVEYOR_GROUP"]: row for row in datasets["recirculation_check"]}
for group in ("Haut", "Sol"):
    row = summary[group]
    check(
        f"bornes métriques {group}",
        0 <= row["RECIRCULATED_PARCELS"] <= row["UNIQUE_READABLE_PARCELS"]
        and 0 <= row["NO_DIMENSIONS"] <= row["UNIQUE_READABLE_PARCELS"]
        and 0 <= row["NO_WEIGHT"] <= row["UNIQUE_READABLE_PARCELS"],
        f"uniques={row['UNIQUE_READABLE_PARCELS']}",
    )
    check(
        f"recalcul indépendant {group}",
        row["UNIQUE_READABLE_PARCELS"] == recirculation[group]["UNIQUE_READABLE_PARCELS"]
        and row["RECIRCULATED_PARCELS"] == recirculation[group]["RECIRCULATED_PARCELS"],
        f"sommaire={row['RECIRCULATED_PARCELS']}; contrôle={recirculation[group]['RECIRCULATED_PARCELS']}",
    )

hourly = [row for row in datasets["hourly"] if row["SORT_DATE"] == default_date]
check(
    "grille horaire complète",
    len(hourly) == 30 and len({row["EVENT_HOUR"] for row in hourly}) == 15,
    f"lignes={len(hourly)}, heures={len({row['EVENT_HOUR'] for row in hourly})}",
)
for group in ("Haut", "Sol"):
    hourly_sum = sum(row["RAW_PASSAGES"] for row in hourly if row["CONVEYOR_GROUP"] == group)
    check(
        f"réconciliation horaire {group}",
        hourly_sum == summary[group]["RAW_SCAN_ROWS"],
        f"horaire={hourly_sum}; sommaire={summary[group]['RAW_SCAN_ROWS']}",
    )

manifest = artifact["manifest"]
filters = manifest.get("filters", [])
check(
    "sélecteur de date",
    len(filters) == 1
    and filters[0]["defaultValue"] == default_date
    and filters[0]["includeAll"] is False
    and {target["dataset"] for target in filters[0]["targets"]} == {"summary_haut", "summary_sol", "hourly"},
    "filtre global sur summary, summary_haut, summary_sol et hourly",
)
card_ids = {card["id"] for card in manifest["cards"]}
required_card_ids = {
    f"card_{group}_{metric}"
    for group in ("haut", "sol")
    for metric in ("total", "recirculated", "dimensions", "weight")
}
check("cartes demandées", required_card_ids <= card_ids, f"cartes={len(card_ids)}")
check("graphique horaire", len(manifest["charts"]) == 1 and manifest["charts"][0]["dataset"] == "hourly", "1 graphique")

source_ids = {source["id"] for source in artifact["sources"]}
referenced_source_ids = {
    item["sourceId"]
    for collection in (manifest["cards"], manifest["charts"], manifest["tables"])
    for item in collection
}
check("sources liées", referenced_source_ids <= source_ids, f"références={sorted(referenced_source_ids)}")
check("HTML généré", HTML_PATH.exists() and HTML_PATH.stat().st_size > 100_000, f"taille={HTML_PATH.stat().st_size if HTML_PATH.exists() else 0}")

serialized = ARTIFACT_PATH.read_text(encoding="utf-8")
secrets_absent = all(token not in serialized for token in ("192.168.1.222", "user_ro", "reporting", "password"))
check("aucun secret de connexion", secrets_absent, "hôte, utilisateur et mot de passe absents")

status = "pass" if all(item["status"] == "pass" for item in checks) else "fail"
RESULTS_PATH.write_text(
    json.dumps({"status": status, "checks": checks}, ensure_ascii=False, indent=2),
    encoding="utf-8",
)
print(json.dumps({"status": status, "checks": checks}, ensure_ascii=False, indent=2))
raise SystemExit(0 if status == "pass" else 1)
