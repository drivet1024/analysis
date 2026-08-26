from __future__ import annotations

from datetime import datetime, timedelta, timezone
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ANALYSIS_DIR = ROOT / "analysis"
DATA_DIR = ANALYSIS_DIR / "conveyor_dashboard_data"
OUTPUT_DIR = ANALYSIS_DIR / "conveyor_dashboard_st_hubert"
ARTIFACT_PATH = OUTPUT_DIR / "artifact.json"
NOTES_PATH = OUTPUT_DIR / "dashboard_notes.md"


def load_json(filename: str):
    return json.loads((DATA_DIR / filename).read_text(encoding="utf-8"))


def load_sql(filename: str) -> str:
    return (ANALYSIS_DIR / filename).read_text(encoding="utf-8-sig").strip()


summary = load_json("summary_all_dates.json")
hourly_raw = load_json("hourly_all_dates.json")
recirculation_check = load_json("recirculation_check.json")
generated_at = datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")

default_sort_date = "2026-07-12"
sort_dates = sorted({row["SORT_DATE"] for row in summary}, reverse=True)
hourly_lookup = {
    (row["SORT_DATE"], row["EVENT_HOUR"], row["CONVEYOR_GROUP"]): row
    for row in hourly_raw
}

hourly = []
for sort_date in sort_dates:
    start = datetime.strptime(sort_date, "%Y-%m-%d").replace(hour=17)
    for offset in range(15):
        timestamp = start + timedelta(hours=offset)
        event_hour = timestamp.strftime("%Y-%m-%d %H:00:00")
        for group in ("Haut", "Sol"):
            row = hourly_lookup.get(
                (sort_date, event_hour, group),
                {
                    "SORT_DATE": sort_date,
                    "EVENT_HOUR": event_hour,
                    "CONVEYOR_GROUP": group,
                    "RAW_PASSAGES": 0,
                    "UNIQUE_READABLE_PARCELS": 0,
                    "UNREADABLE_SCAN_ROWS": 0,
                    "MISSING_DIMENSION_SCAN_ROWS": 0,
                    "MISSING_WEIGHT_SCAN_ROWS": 0,
                },
            )
            hourly.append(
                {
                    **row,
                    "HOUR_LABEL": f"{timestamp.hour} h",
                    "CONVEYOR_LABEL": f"Convoyeur du {group.lower()}",
                }
            )

sources = [
    {
        "id": "src_summary",
        "label": "Indicateurs des convoyeurs de St-Hubert",
        "query": {
            "engine": "MySQL 8.4",
            "language": "SQL",
            "executed_at": generated_at,
            "description": "Compteurs officiels et indicateurs dédupliqués du convoyeur du haut et du convoyeur du sol.",
            "tables_used": ["nationex.conveyor_status", "nationex.parcel_scan_history"],
            "filters": [
                "DEPOT_ID = 1 (St-Hubert)",
                "Dates de tri complètes du 1er juin au 15 juillet 2026, journées sans passage exclues",
                "Fenêtre de chaque date : 17 h au lendemain 8 h, heure EDT",
                "Convoyeur du haut = lignes techniques 0 et 1; convoyeur du sol = ligne technique 3",
            ],
            "metric_definitions": [
                "Total = somme de nb_parcel dans conveyor_status.",
                "Recirculé = parcel_id lisible ayant plus d'un scan dans le même groupe de convoyeur.",
                "Sans dimensions = dernier scan du colis lisible avec au moins une dimension nulle ou non positive.",
                "Sans poids = dernier scan du colis lisible avec un poids nul ou non positif.",
                "Les taux de qualité utilisent les colis lisibles uniques comme dénominateur.",
            ],
            "sql": load_sql("conveyor_dashboard_summary_all_dates.sql"),
        },
    },
    {
        "id": "src_hourly",
        "label": "Passages bruts par heure",
        "query": {
            "engine": "MySQL 8.4",
            "language": "SQL",
            "executed_at": generated_at,
            "description": "Scans bruts par heure et par groupe de convoyeur pendant la fenêtre opérationnelle.",
            "tables_used": ["nationex.parcel_scan_history"],
            "filters": [
                "DEPOT_ID = 1 (St-Hubert)",
                "LINE_ID IN (0, 1, 3)",
                "Dates de tri complètes du 1er juin au 15 juillet 2026, journées sans passage exclues",
                "De 17 h inclus à 8 h exclu pour chaque date de tri, heure EDT",
            ],
            "metric_definitions": [
                "Passages bruts = nombre de lignes de parcel_scan_history.",
                "Une valeur zéro est ajoutée au graphique lorsqu'un convoyeur n'a aucun scan pendant une heure observée.",
            ],
            "sql": load_sql("conveyor_dashboard_hourly_all_dates.sql"),
        },
    },
]

cards = []
for group in ("Haut", "Sol"):
    slug = group.lower()
    cards.extend(
        [
            {
                "id": f"card_{slug}_total",
                "dataset": f"summary_{slug}",
                "sourceId": "src_summary",
                "description": "Compteur officiel de passages; la puce montre les colis lisibles uniques.",
                "metrics": [
                    {"label": "Passages officiels", "field": "OFFICIAL_PASSAGES", "format": "compact"},
                    {"label": "Colis lisibles uniques", "field": "UNIQUE_READABLE_PARCELS", "format": "compact"},
                ],
            },
            {
                "id": f"card_{slug}_recirculated",
                "dataset": f"summary_{slug}",
                "sourceId": "src_summary",
                "description": "Colis lisibles vus plus d'une fois sur ce groupe de convoyeur.",
                "metrics": [
                    {"label": "Colis recirculés", "field": "RECIRCULATED_PARCELS", "format": "compact"},
                    {"label": "Taux des colis lisibles", "field": "RECIRCULATION_RATE", "format": "percent"},
                ],
            },
            {
                "id": f"card_{slug}_dimensions",
                "dataset": f"summary_{slug}",
                "sourceId": "src_summary",
                "description": "Au moins une dimension absente ou non positive au dernier scan du colis.",
                "metrics": [
                    {"label": "Sans dimensions", "field": "NO_DIMENSIONS", "format": "compact"},
                    {"label": "Taux des colis lisibles", "field": "NO_DIMENSIONS_RATE", "format": "percent"},
                ],
            },
            {
                "id": f"card_{slug}_weight",
                "dataset": f"summary_{slug}",
                "sourceId": "src_summary",
                "description": "Poids absent ou non positif au dernier scan du colis.",
                "metrics": [
                    {"label": "Sans poids", "field": "NO_WEIGHT", "format": "compact"},
                    {"label": "Taux des colis lisibles", "field": "NO_WEIGHT_RATE", "format": "percent"},
                ],
            },
        ]
    )

charts = [
    {
        "id": "chart_hourly",
        "title": "Nombre de passages par heure",
        "subtitle": "Passages bruts de 17 h à 7 h; heure locale EDT",
        "showDescription": True,
        "intent": "trend",
        "question": "Comment le débit horaire se répartit-il entre le convoyeur du haut et celui du sol?",
        "rationale": "Deux lignes permettent de comparer le profil horaire et les pointes de chaque convoyeur.",
        "comparisonContext": {
            "grain": "heure et groupe de convoyeur",
            "unit": "passages bruts",
            "denominator": "lignes de parcel_scan_history",
        },
        "type": "line",
        "dataset": "hourly",
        "sourceId": "src_hourly",
        "encodings": {
            "x": {"field": "HOUR_LABEL", "type": "ordinal", "label": "Heure locale"},
            "y": {"field": "RAW_PASSAGES", "type": "quantitative", "aggregate": "none", "format": "compact", "label": "Passages bruts"},
            "color": {"field": "CONVEYOR_LABEL", "type": "nominal", "label": "Convoyeur"},
            "tooltip": [
                {"field": "UNIQUE_READABLE_PARCELS", "type": "quantitative", "format": "compact", "label": "Colis lisibles uniques dans l'heure"},
                {"field": "UNREADABLE_SCAN_ROWS", "type": "quantitative", "format": "compact", "label": "Scans non lisibles"},
                {"field": "MISSING_DIMENSION_SCAN_ROWS", "type": "quantitative", "format": "compact", "label": "Scans sans dimensions"},
                {"field": "MISSING_WEIGHT_SCAN_ROWS", "type": "quantitative", "format": "compact", "label": "Scans sans poids"},
            ],
        },
        "palette": {"kind": "categorical", "name": "blue-orange"},
        "legend": {"position": "bottom", "title": "Convoyeur"},
        "labels": {"values": "none"},
        "settings": {"sort": "none", "showPoints": "always"},
        "layout": "full",
        "maxRows": 20,
        "surface": {"surface": "export", "viewMode": "both"},
    }
]

tables = [
    {
        "id": "table_comparison",
        "title": "Comparaison détaillée",
        "subtitle": "Les trois taux utilisent les colis lisibles uniques comme dénominateur",
        "showDescription": True,
        "dataset": "summary",
        "sourceId": "src_summary",
        "defaultSort": {"field": "OFFICIAL_PASSAGES", "direction": "desc"},
        "density": "spacious",
        "layout": "full",
        "columns": [
            {"field": "CONVEYOR_GROUP", "label": "Convoyeur", "type": "text"},
            {"field": "OFFICIAL_PASSAGES", "label": "Total officiel", "format": "number"},
            {"field": "UNIQUE_READABLE_PARCELS", "label": "Lisibles uniques", "format": "number"},
            {"field": "RECIRCULATED_PARCELS", "label": "Recirculés", "format": "number"},
            {"field": "RECIRCULATION_RATE", "label": "Taux recirculé", "format": "percent"},
            {"field": "NO_DIMENSIONS", "label": "Sans dimensions", "format": "number"},
            {"field": "NO_DIMENSIONS_RATE", "label": "Taux sans dimensions", "format": "percent"},
            {"field": "NO_WEIGHT", "label": "Sans poids", "format": "number"},
            {"field": "NO_WEIGHT_RATE", "label": "Taux sans poids", "format": "percent"},
        ],
    }
]

blocks = [
    {
        "id": "title",
        "type": "markdown",
        "body": "# Dashboard des convoyeurs — St-Hubert",
        "layout": "full",
    },
    {
        "id": "scope",
        "type": "markdown",
        "layout": "full",
        "body": (
            "**Choisissez la date de tri dans le sélecteur ci-dessus** pour faire vos propres analyses. "
            "Les journées complètes du 1er juin au 15 juillet 2026 sont disponibles; chacune couvre 17 h à 8 h le lendemain (EDT). "
            "Correspondance utilisée : **haut = lignes 0 + 1; sol = ligne 3.**"
        ),
    },
    {"id": "haut_title", "type": "markdown", "body": "## Convoyeur du haut", "layout": "full"},
    {
        "id": "haut_metrics",
        "type": "metric-strip",
        "cardIds": [
            "card_haut_total",
            "card_haut_recirculated",
            "card_haut_dimensions",
            "card_haut_weight",
        ],
        "layout": "full",
    },
    {"id": "sol_title", "type": "markdown", "body": "## Convoyeur du sol", "layout": "full"},
    {
        "id": "sol_metrics",
        "type": "metric-strip",
        "cardIds": [
            "card_sol_total",
            "card_sol_recirculated",
            "card_sol_dimensions",
            "card_sol_weight",
        ],
        "layout": "full",
    },
    {
        "id": "small_parcels_note",
        "type": "markdown",
        "sourceId": "src_summary",
        "layout": "full",
        "body": (
            "*Lecture du poids au sol : les 153 colis sans poids sont un constat de donnée, pas nécessairement une panne. "
            "Les très petits colis peuvent être sous le seuil de sensibilité de la balance.*"
        ),
    },
    {
        "id": "hourly_title",
        "type": "markdown",
        "layout": "full",
        "body": "## Débit horaire",
    },
    {"id": "hourly_chart_block", "type": "chart", "chartId": "chart_hourly", "layout": "full"},
    {"id": "comparison_title", "type": "markdown", "body": "## Détail des indicateurs", "layout": "full"},
    {"id": "comparison_table_block", "type": "table", "tableId": "table_comparison", "layout": "full"},
    {
        "id": "definitions",
        "type": "markdown",
        "layout": "full",
        "body": (
            "## Définitions et limites\n\n"
            "- **Total :** passages officiels de `conveyor_status`; les passages horaires viennent de l'historique brut.\n"
            "- **Recirculé :** colis lisible vu plus d'une fois sur le même groupe de convoyeur. C'est un indicateur de relecture/recirculation, pas une preuve d'anomalie.\n"
            "- **Sans dimensions / sans poids :** état du dernier scan de chaque colis lisible; ces catégories peuvent se chevaucher.\n"
            "- Le total officiel inclut aussi les non-lectures et peut donc dépasser le nombre de colis lisibles uniques."
        ),
    },
]

artifact = {
    "surface": "dashboard",
    "manifest": {
        "version": 1,
        "surface": "dashboard",
        "title": "Dashboard des convoyeurs — St-Hubert",
        "description": "Vue comparative du volume, de la recirculation, des dimensions, du poids et du débit horaire.",
        "generatedAt": generated_at,
        "filters": [
            {
                "id": "filter_sort_date",
                "label": "Date de tri",
                "dataset": "summary",
                "field": "SORT_DATE",
                "defaultValue": default_sort_date,
                "includeAll": False,
                "targets": [
                    {"dataset": "summary_haut", "field": "SORT_DATE"},
                    {"dataset": "summary_sol", "field": "SORT_DATE"},
                    {"dataset": "hourly", "field": "SORT_DATE"},
                ],
            }
        ],
        "blocks": blocks,
        "cards": cards,
        "charts": charts,
        "tables": tables,
        "sources": sources,
    },
    "snapshot": {
        "version": 1,
        "generatedAt": generated_at,
        "status": "ready",
        "datasets": {
            "summary": summary,
            "summary_haut": [row for row in summary if row["CONVEYOR_GROUP"] == "Haut"],
            "summary_sol": [row for row in summary if row["CONVEYOR_GROUP"] == "Sol"],
            "hourly": hourly,
            "recirculation_check": recirculation_check,
        },
    },
    "sources": sources,
}

OUTPUT_DIR.mkdir(exist_ok=True)
ARTIFACT_PATH.write_text(json.dumps(artifact, ensure_ascii=False, indent=2), encoding="utf-8")
NOTES_PATH.write_text(
    """# Notes de construction du dashboard

- Public : opérations et gestion de St-Hubert.
- Surface : dashboard HTML portable.
- Le sélecteur de date filtre toutes les cartes, le graphique et le tableau.
- Les dates complètes du 1er juin au 15 juillet 2026 sont préchargées; les journées à zéro passage sont exclues.
- Convoyeur du haut : lignes techniques 0 et 1; convoyeur du sol : ligne technique 3.
- Les cartes utilisent le total officiel et des indicateurs calculés sur les colis lisibles uniques.
- Le graphique horaire utilise les passages bruts sur une grille fixe de 17 h à 7 h; les heures sans scan sont affichées à zéro.
- Palette : deux racines visuelles, bleu et orange, pour distinguer les convoyeurs.
- Le contrôle indépendant de recirculation doit reproduire les populations uniques et recirculées du sommaire.
""",
    encoding="utf-8",
)

print(ARTIFACT_PATH)
