from __future__ import annotations

from collections import defaultdict
from datetime import datetime
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ANALYSIS_DIR = ROOT / "analysis"
INPUT = ANALYSIS_DIR / "conveyor_st_hubert_results_2026-07-12.json"
OUTPUT_DIR = ANALYSIS_DIR / "conveyor_st_hubert_report_2026-07-12"
ARTIFACT_PATH = OUTPUT_DIR / "artifact.json"
NOTES_PATH = OUTPUT_DIR / "report_notes.md"


def load_sql(filename: str) -> str:
    return (ANALYSIS_DIR / filename).read_text(encoding="utf-8-sig").strip()


def fraction(value: float) -> float:
    return round(value / 100.0, 6)


payload = json.loads(INPUT.read_text(encoding="utf-8"))
data = payload["results"]
generated_at = payload["source"]["extracted_at_utc"]

sundays = data["sunday_totals"]
current = sundays[-1]
prior = sundays[:-1]
prior_parcels = sum(row["PARCELS"] for row in prior)
prior_average_volume = prior_parcels / len(prior)

volume_vs_prior_average = current["PARCELS"] / prior_average_volume - 1
volume_vs_previous_sunday = current["PARCELS"] / prior[-1]["PARCELS"] - 1
prior_reject_rate = sum(row["REJECTED"] for row in prior) / prior_parcels
prior_noread_rate = sum(row["NOREAD"] for row in prior) / prior_parcels
prior_scale_rate = sum(row["SCALE_ERRORS"] for row in prior) / prior_parcels

headline_status = [{
    **current,
    "PARCELS_VS_PRIOR_AVERAGE": round(volume_vs_prior_average, 6),
    "PARCELS_VS_PREVIOUS_SUNDAY": round(volume_vs_previous_sunday, 6),
    "REJECT_RATE": fraction(current["REJECT_RATE_PCT"]),
    "REJECT_RATE_DELTA_VS_PRIOR": round(fraction(current["REJECT_RATE_PCT"]) - prior_reject_rate, 6),
    "NOREAD_RATE": fraction(current["NOREAD_RATE_PCT"]),
    "NOREAD_RATE_DELTA_VS_PRIOR": round(fraction(current["NOREAD_RATE_PCT"]) - prior_noread_rate, 6),
    "SCALE_ERROR_RATE": fraction(current["SCALE_ERROR_RATE_PCT"]),
    "SCALE_ERROR_RATE_DELTA_VS_PRIOR": round(fraction(current["SCALE_ERROR_RATE_PCT"]) - prior_scale_rate, 6),
    "PRIOR_AVERAGE_VOLUME": round(prior_average_volume),
    "PRIOR_REJECT_RATE": round(prior_reject_rate, 6),
    "PRIOR_NOREAD_RATE": round(prior_noread_rate, 6),
    "PRIOR_SCALE_RATE": round(prior_scale_rate, 6),
}]

hourly_rows = []
hour_totals: dict[str, int] = defaultdict(int)
for row in data["hourly_main_lines"]:
    timestamp = datetime.strptime(row["EVENT_HOUR"], "%Y-%m-%d %H:%M:%S")
    label = f"{'Dim.' if timestamp.day == 12 else 'Lun.'} {timestamp.hour} h"
    enriched = {
        **row,
        "EVENT_HOUR_LABEL": label,
        "LINE_LABEL": f"Ligne {row['LINE_ID']}",
        "READABLE_RATE": round(row["READABLE_SCAN_ROWS"] / row["SCAN_ROWS"], 6),
    }
    hourly_rows.append(enriched)
    hour_totals[row["EVENT_HOUR"]] += row["SCAN_ROWS"]

peak_hour, peak_scans = max(hour_totals.items(), key=lambda item: item[1])
peak_timestamp = datetime.strptime(peak_hour, "%Y-%m-%d %H:%M:%S")
headline_hourly = [{
    "PEAK_SCAN_ROWS": peak_scans,
    "PEAK_HOUR_LABEL": f"Lun. {peak_timestamp.hour} h à {peak_timestamp.hour} h 59",
    "RAW_SCAN_ROWS": sum(hour_totals.values()),
    "PEAK_SHARE": round(peak_scans / sum(hour_totals.values()), 6),
}]

quality_rows = []
quality_metrics = [
    ("Rejets", "REJECT_RATE_PCT", "REJECTED"),
    ("Non-lectures", "NOREAD_RATE_PCT", "NOREAD"),
    ("Erreurs de balance", "SCALE_ERROR_RATE_PCT", "SCALE_ERRORS"),
]
for row in sundays:
    timestamp = datetime.fromisoformat(row["SORT_DATE"])
    date_label = timestamp.strftime("%d %b").replace("Jun", "juin").replace("Jul", "juil.")
    for metric_label, rate_field, count_field in quality_metrics:
        quality_rows.append({
            "SORT_DATE": row["SORT_DATE"],
            "DATE_LABEL": date_label,
            "METRIC": metric_label,
            "RATE": fraction(row[rate_field]),
            "ISSUE_COUNT": row[count_field],
            "PARCELS": row["PARCELS"],
        })

line_rows = []
for row in data["line_summary"]:
    line_rows.append({
        **row,
        "LINE_LABEL": f"Ligne {row['line_id']}",
        "VOLUME_SHARE": round(row["nb_parcel"] / current["PARCELS"], 6),
        "REJECT_CONTRIBUTION": round(row["nb_rejected"] / current["REJECTED"], 6),
        "NOREAD_CONTRIBUTION": round(row["nb_noread"] / current["NOREAD"], 6),
        "REJECT_RATE": fraction(row["REJECT_RATE_PCT"]),
        "NOREAD_RATE": fraction(row["NOREAD_RATE_PCT"]),
        "DIMENSION_ERROR_RATE": fraction(row["DIMENSION_ERROR_RATE_PCT"]),
        "SCALE_ERROR_RATE": fraction(row["SCALE_ERROR_RATE_PCT"]),
    })

routing_summary = data["routing_events"]
routing = routing_summary[0]
destination_total = sum(row["UNIQUE_PARCELS"] for row in data["destination_depots"])
destination_rows = []
cumulative = 0
for rank, row in enumerate(data["destination_depots"], start=1):
    cumulative += row["UNIQUE_PARCELS"]
    destination_rows.append({
        **row,
        "RANK": rank,
        "SHARE": round(row["UNIQUE_PARCELS"] / destination_total, 6),
        "CUMULATIVE_SHARE": round(cumulative / destination_total, 6),
    })

top_destination_rows = [
    row for row in destination_rows if row["DESTINATION_DEPOT"] != "NON RENSEIGNÉ"
][:10]
top_four_destination_share = sum(row["UNIQUE_PARCELS"] for row in destination_rows[:4]) / destination_total
top_two_destination_share = sum(row["UNIQUE_PARCELS"] for row in destination_rows[:2]) / destination_total

chute_rows = []
top_chute_events = sum(row["ROUTING_EVENTS"] for row in data["top_chutes"][:5])
for rank, row in enumerate(data["top_chutes"], start=1):
    chute_rows.append({
        **row,
        "RANK": rank,
        "LINE_CHUTE": f"L{row['LINE_ID']} · chute {row['CHUTE_NO']}",
        "EVENT_SHARE": round(row["ROUTING_EVENTS"] / routing["ROUTING_EVENTS"], 6),
    })

source_specs = [
    {
        "id": "src_status",
        "label": "Compteurs officiels du convoyeur — St-Hubert",
        "query": {
            "engine": "MySQL 8.4",
            "language": "SQL",
            "executed_at": generated_at,
            "description": "Totaux et taux des cinq dimanches, agrégés à partir du statut final de chaque ligne.",
            "tables_used": ["nationex.conveyor_status"],
            "filters": [
                "DEPOT_ID = 1 (ST-HUBERT)",
                "Dimanches du 14 juin au 12 juillet 2026",
                "Une ligne finale par date de tri et ligne de convoyeur",
            ],
            "metric_definitions": [
                "Passages = somme de nb_parcel sur les lignes 0, 1 et 3.",
                "Taux de rejet = somme(nb_rejected) / somme(nb_parcel).",
                "Taux de non-lecture = somme(nb_noread) / somme(nb_parcel).",
                "Taux d'erreur de balance = somme(nb_scale_error) / somme(nb_parcel).",
                "Repère précédent = taux pondéré par les passages des quatre dimanches antérieurs.",
            ],
            "sql": load_sql("conveyor_st_hubert_sunday_totals.sql"),
        },
    },
    {
        "id": "src_hourly",
        "label": "Historique brut horaire — lignes principales",
        "query": {
            "engine": "MySQL 8.4",
            "language": "SQL",
            "executed_at": generated_at,
            "description": "Scans bruts par heure et par ligne pendant la fenêtre opérationnelle.",
            "tables_used": ["nationex.parcel_scan_history"],
            "filters": [
                "DEPOT_ID = 1",
                "LINE_ID IN (0, 1, 3)",
                "Du 12 juillet 2026 à 17 h au 13 juillet 2026 à 8 h, heure EDT",
            ],
            "metric_definitions": [
                "Scans bruts = nombre de lignes de parcel_scan_history.",
                "Colis lisibles uniques = nombre distinct de parcel_id non nul et non zéro.",
            ],
            "sql": load_sql("conveyor_st_hubert_hourly_main_lines.sql"),
        },
    },
    {
        "id": "src_line",
        "label": "Qualité par ligne — 12 juillet 2026",
        "query": {
            "engine": "MySQL 8.4",
            "language": "SQL",
            "executed_at": generated_at,
            "description": "Compteurs et taux finaux de chaque ligne du convoyeur de St-Hubert.",
            "tables_used": ["nationex.conveyor_status"],
            "filters": ["DEPOT_ID = 1", "SORT_DATE = 2026-07-12"],
            "metric_definitions": [
                "Chaque taux de ligne utilise nb_parcel de la même ligne comme dénominateur.",
            ],
            "sql": load_sql("conveyor_st_hubert_line_summary.sql"),
        },
    },
    {
        "id": "src_routing",
        "label": "Événements de routage dédupliqués",
        "query": {
            "engine": "MySQL 8.4",
            "language": "SQL",
            "executed_at": generated_at,
            "description": "Colis routés, événements multiples et passages sur plusieurs lignes ou sources.",
            "tables_used": ["nationex.parcel_history"],
            "filters": [
                "DEPOT_ID = 1",
                "SOURCE_TYPE = 200 (CONVOYEUR)",
                "Lignes ou sources principales 0, 1 et 3",
                "Fenêtre du 12 juillet 17 h au 13 juillet 8 h, heure EDT",
            ],
            "metric_definitions": [
                "Colis routés uniques = nombre distinct de PARCEL_ID ayant au moins un événement de routage.",
                "Événements supplémentaires = somme(nombre d'événements par colis - 1).",
            ],
            "sql": load_sql("conveyor_st_hubert_routing_events.sql"),
        },
    },
    {
        "id": "src_destinations",
        "label": "Destinations dérivées des colis routés",
        "query": {
            "engine": "MySQL 8.4",
            "language": "SQL",
            "executed_at": generated_at,
            "description": "Dépôt de destination dérivé de l'expédition et de la table de localisation postale.",
            "tables_used": [
                "nationex.parcel_history",
                "nationex.shipment",
                "nationex.location",
                "nationex.depot",
            ],
            "filters": [
                "Même population de colis routés uniques que src_routing",
                "Jointure shipment par (SHIPPING_ID, EXP_DATE)",
                "Jointure location par code postal normalisé",
            ],
            "metric_definitions": [
                "Un colis est attribué à la destination du dernier événement de routage de la fenêtre.",
                "Couverture = 8 333 colis renseignés sur 8 340; 7 restent non renseignés.",
            ],
            "sql": load_sql("conveyor_st_hubert_destination_derived.sql"),
        },
    },
    {
        "id": "src_chutes",
        "label": "Principales combinaisons ligne-chute",
        "query": {
            "engine": "MySQL 8.4",
            "language": "SQL",
            "executed_at": generated_at,
            "description": "Les douze combinaisons ligne-chute ayant le plus d'événements de routage.",
            "tables_used": ["nationex.parcel_history"],
            "filters": [
                "Même fenêtre et même population d'événements de routage que src_routing",
                "Limite aux 12 combinaisons ayant le plus d'événements",
            ],
            "metric_definitions": [
                "Part des événements = événements de la combinaison / 9 529 événements de routage.",
            ],
            "sql": load_sql("conveyor_st_hubert_top_chutes.sql"),
        },
    },
]

cards = [
    {
        "id": "card_volume",
        "dataset": "headline_status",
        "sourceId": "src_status",
        "description": "Somme des compteurs finaux des lignes 0, 1 et 3.",
        "metrics": [
            {"label": "Passages officiels", "field": "PARCELS", "format": "compact"},
            {"label": "vs moyenne 4 dim.", "field": "PARCELS_VS_PRIOR_AVERAGE", "format": "percent", "signed": True},
            {"label": "vs 5 juillet", "field": "PARCELS_VS_PREVIOUS_SUNDAY", "format": "percent", "signed": True},
        ],
    },
    {
        "id": "card_peak",
        "dataset": "headline_hourly",
        "sourceId": "src_hourly",
        "description": "Pointe observée entre minuit et 0 h 59, heure EDT.",
        "metrics": [
            {"label": "Scans à l'heure de pointe", "field": "PEAK_SCAN_ROWS", "format": "compact"},
            {"label": "Part du flux brut", "field": "PEAK_SHARE", "format": "percent"},
        ],
    },
    {
        "id": "card_reject",
        "dataset": "headline_status",
        "sourceId": "src_status",
        "description": "Rejets divisés par les passages officiels.",
        "metrics": [
            {"label": "Taux de rejet", "field": "REJECT_RATE", "format": "percent"},
            {"label": "vs repère 4 dim.", "field": "REJECT_RATE_DELTA_VS_PRIOR", "format": "percent", "signed": True},
        ],
    },
    {
        "id": "card_noread",
        "dataset": "headline_status",
        "sourceId": "src_status",
        "description": "Non-lectures divisées par les passages officiels.",
        "metrics": [
            {"label": "Taux de non-lecture", "field": "NOREAD_RATE", "format": "percent"},
            {"label": "vs repère 4 dim.", "field": "NOREAD_RATE_DELTA_VS_PRIOR", "format": "percent", "signed": True},
        ],
    },
    {
        "id": "card_scale",
        "dataset": "headline_status",
        "sourceId": "src_status",
        "description": "Erreurs de balance divisées par les passages officiels.",
        "metrics": [
            {"label": "Erreurs de balance", "field": "SCALE_ERROR_RATE", "format": "percent"},
            {"label": "vs repère 4 dim.", "field": "SCALE_ERROR_RATE_DELTA_VS_PRIOR", "format": "percent", "signed": True},
        ],
    },
    {
        "id": "card_unique",
        "dataset": "routing_summary",
        "sourceId": "src_routing",
        "description": "Colis distincts ayant reçu au moins un événement de routage réussi.",
        "metrics": [
            {"label": "Colis routés uniques", "field": "UNIQUE_ROUTED_PARCELS", "format": "compact"},
            {"label": "Avec événements multiples", "field": "MULTI_EVENT_PARCEL_RATE", "format": "percent"},
        ],
    },
]

routing_summary[0]["MULTI_EVENT_PARCEL_RATE"] = fraction(routing["MULTI_EVENT_PARCEL_RATE_PCT"])

charts = [
    {
        "id": "chart_hourly",
        "title": "Passages bruts par heure et par ligne",
        "subtitle": "Fenêtre opérationnelle du 12 juillet; pointe de 1 999 scans entre minuit et 0 h 59",
        "showDescription": True,
        "intent": "composition",
        "question": "Quand la charge a-t-elle culminé et comment était-elle répartie entre les lignes?",
        "rationale": "Un diagramme en barres empilées montre simultanément le débit horaire et la contribution des trois lignes.",
        "comparisonContext": {
            "grain": "heure et ligne",
            "unit": "scans bruts",
            "denominator": "lignes de parcel_scan_history",
        },
        "type": "stackedBar",
        "dataset": "hourly_main_lines",
        "sourceId": "src_hourly",
        "encodings": {
            "x": {"field": "EVENT_HOUR_LABEL", "type": "ordinal", "label": "Heure locale"},
            "y": {"field": "SCAN_ROWS", "type": "quantitative", "aggregate": "sum", "format": "compact", "label": "Scans bruts"},
            "color": {"field": "LINE_LABEL", "type": "nominal", "label": "Ligne"},
            "tooltip": [
                {"field": "READABLE_SCAN_ROWS", "type": "quantitative", "format": "compact", "label": "Scans lisibles"},
                {"field": "UNREADABLE_SCAN_ROWS", "type": "quantitative", "format": "compact", "label": "Scans non lisibles"},
            ],
        },
        "palette": {"kind": "categorical", "name": "blue-orange-pink"},
        "legend": {"position": "bottom", "title": "Ligne"},
        "labels": {"values": "none"},
        "settings": {"groupMode": "stacked", "sort": "none"},
        "layout": "full",
        "maxRows": 40,
        "surface": {"surface": "export", "viewMode": "both"},
    },
    {
        "id": "chart_quality",
        "title": "Taux de défaut sur cinq dimanches",
        "subtitle": "Rejets, non-lectures et erreurs de balance en proportion des passages officiels",
        "showDescription": True,
        "intent": "comparison",
        "question": "Les taux de défaut du 12 juillet sont-ils élevés par rapport aux dimanches précédents?",
        "rationale": "Des barres groupées comparent trois taux de même unité sur cinq périodes discrètes sans suggérer une tendance longue.",
        "comparisonContext": {
            "baseline": "quatre dimanches précédents",
            "grain": "date de tri et type de défaut",
            "unit": "part des passages",
            "denominator": "passages officiels de chaque dimanche",
        },
        "type": "bar",
        "dataset": "quality_comparison",
        "sourceId": "src_status",
        "encodings": {
            "x": {"field": "DATE_LABEL", "type": "ordinal", "label": "Dimanche"},
            "y": {"field": "RATE", "type": "quantitative", "aggregate": "none", "format": "percent", "label": "Taux"},
            "color": {"field": "METRIC", "type": "nominal", "label": "Défaut"},
            "tooltip": [
                {"field": "ISSUE_COUNT", "type": "quantitative", "format": "compact", "label": "Occurrences"},
                {"field": "PARCELS", "type": "quantitative", "format": "compact", "label": "Passages"},
            ],
        },
        "palette": {"kind": "categorical", "name": "blue-orange-pink"},
        "legend": {"position": "bottom", "title": "Type de défaut"},
        "labels": {"values": "auto"},
        "settings": {"groupMode": "grouped", "sort": "none"},
        "layout": "full",
        "maxRows": 25,
        "surface": {"surface": "export", "viewMode": "both"},
    },
    {
        "id": "chart_destinations",
        "title": "Principales destinations des colis routés",
        "subtitle": "Top 10; destination dérivée de l'expédition et du code postal",
        "showDescription": True,
        "intent": "comparison",
        "question": "Quels dépôts de destination représentent la plus grande part des colis routés?",
        "rationale": "Des barres horizontales classées rendent les noms de dépôts lisibles et montrent la concentration du flux.",
        "comparisonContext": {
            "grain": "dépôt de destination",
            "unit": "colis routés uniques",
            "denominator": "8 340 colis routés uniques",
        },
        "type": "horizontalBar",
        "dataset": "top_destinations",
        "sourceId": "src_destinations",
        "encodings": {
            "x": {"field": "DESTINATION_DEPOT", "type": "nominal", "label": "Dépôt de destination"},
            "y": {"field": "UNIQUE_PARCELS", "type": "quantitative", "aggregate": "none", "format": "compact", "label": "Colis routés uniques"},
            "tooltip": [
                {"field": "SHARE", "type": "quantitative", "format": "percent", "label": "Part"},
                {"field": "CUMULATIVE_SHARE", "type": "quantitative", "format": "percent", "label": "Part cumulée"},
            ],
        },
        "palette": {"kind": "categorical", "name": "blue"},
        "labels": {"values": "all"},
        "settings": {"orientation": "horizontal", "sort": "descending"},
        "layout": "full",
        "maxRows": 12,
        "surface": {"surface": "export", "viewMode": "both"},
    },
]

tables = [
    {
        "id": "table_lines",
        "title": "Qualité et contribution par ligne",
        "subtitle": "Compteurs finaux du 12 juillet; les taux utilisent les passages de chaque ligne",
        "showDescription": True,
        "dataset": "line_summary",
        "sourceId": "src_line",
        "defaultSort": {"field": "nb_parcel", "direction": "desc"},
        "density": "spacious",
        "layout": "full",
        "columns": [
            {"field": "LINE_LABEL", "label": "Ligne", "type": "text"},
            {"field": "nb_parcel", "label": "Passages", "format": "number"},
            {"field": "VOLUME_SHARE", "label": "Part du volume", "format": "percent"},
            {"field": "NOREAD_RATE", "label": "Non-lecture", "format": "percent"},
            {"field": "REJECT_RATE", "label": "Rejet", "format": "percent"},
            {"field": "DIMENSION_ERROR_RATE", "label": "Erreur dimension", "format": "percent"},
            {"field": "SCALE_ERROR_RATE", "label": "Erreur balance", "format": "percent"},
        ],
    },
    {
        "id": "table_chutes",
        "title": "Combinaisons ligne-chute les plus sollicitées",
        "subtitle": "Top 12 par nombre d'événements de routage pendant la fenêtre opérationnelle",
        "showDescription": True,
        "dataset": "top_chutes",
        "sourceId": "src_chutes",
        "defaultSort": {"field": "ROUTING_EVENTS", "direction": "desc"},
        "density": "dense",
        "layout": "full",
        "columns": [
            {"field": "LINE_CHUTE", "label": "Ligne · chute", "type": "text"},
            {"field": "ROUTING_EVENTS", "label": "Événements", "format": "number"},
            {"field": "UNIQUE_PARCELS", "label": "Colis uniques", "format": "number"},
            {"field": "EVENT_SHARE", "label": "Part des événements", "format": "percent"},
        ],
    },
]

blocks = [
    {"id": "title", "type": "markdown", "body": "# Convoyeur de St-Hubert — dimanche 12 juillet 2026", "layout": "full"},
    {
        "id": "executive_summary",
        "type": "markdown",
        "layout": "full",
        "body": (
            "## Executive Summary\n\n"
            "- **Le volume était élevé :** 10,35k passages, soit +18,5 % par rapport à la moyenne des quatre dimanches précédents et +31,6 % par rapport au 5 juillet.\n"
            "- **La qualité s'est détériorée :** les rejets (7,25 %), les non-lectures (6,33 %) et les erreurs de balance (6,07 %) atteignent chacun leur sommet sur les cinq dimanches observés.\n"
            "- **La ligne 1 est la priorité :** elle porte 46,5 % du volume, mais concentre 74,9 % des rejets et 68,5 % des non-lectures.\n"
            "- **La recirculation mérite un traçage :** 882 des 8,34k colis routés uniques ont plusieurs événements de routage; 416 sont vus sur plus d'une ligne ou source."
        ),
    },
    {"id": "headline_metrics", "type": "metric-strip", "cardIds": [card["id"] for card in cards], "layout": "full"},
    {
        "id": "volume_story",
        "type": "markdown",
        "layout": "full",
        "body": (
            "## Le volume élevé a culminé à minuit\n\n"
            "**La pointe atteint 1 999 scans bruts entre minuit et 0 h 59**, après deux vagues à 19 h (1 624) et 22 h (1 781). La chute à 20 h puis la reprise à 21-22 h indiquent une cadence discontinue plutôt qu'un flux uniforme.\n\n"
            "Les compteurs officiels totalisent 10 350 passages et l'historique brut 10 330 scans sur les lignes principales, un écart de seulement 20 (0,19 %). Cette concordance soutient l'usage des compteurs officiels pour les taux et de l'historique brut pour le profil horaire."
        ),
    },
    {"id": "hourly_chart_block", "type": "chart", "chartId": "chart_hourly", "layout": "full"},
    {
        "id": "line_story",
        "type": "markdown",
        "sourceId": "src_line",
        "layout": "full",
        "body": (
            "## La ligne 1 concentre la majorité des défauts\n\n"
            "**Avec 4 811 passages, la ligne 1 représente 46,5 % du volume, mais 74,9 % des rejets et 68,5 % des non-lectures.** Ses taux atteignent 11,68 % de rejet et 9,33 % de non-lecture.\n\n"
            "La ligne 3 présente le profil inverse : seulement 0,05 % de rejet, mais 9,83 % d'erreurs de balance. Cela suggère deux pistes distinctes — lecture et rejet sur la ligne 1, mesure du poids sur la ligne 3."
        ),
    },
    {"id": "line_table_block", "type": "table", "tableId": "table_lines", "layout": "full"},
    {
        "id": "quality_story",
        "type": "markdown",
        "sourceId": "src_status",
        "layout": "full",
        "body": (
            "## Trois indicateurs de qualité se dégradent en même temps\n\n"
            "**Le taux de rejet dépasse de 2,06 points le repère pondéré des quatre dimanches précédents; la non-lecture le dépasse de 1,44 point et les erreurs de balance de 1,94 point.** Le mouvement simultané réduit la probabilité qu'il s'agisse d'un seul code d'exception isolé.\n\n"
            "Les erreurs de dimensionnement sont à 1,61 %, dans la plage récente observée après l'anomalie du 14 juin. Elles ne constituent donc pas le premier signal à investiguer cette fois-ci."
        ),
    },
    {"id": "quality_chart_block", "type": "chart", "chartId": "chart_quality", "layout": "full"},
    {
        "id": "routing_story",
        "type": "markdown",
        "sourceId": "src_routing",
        "layout": "full",
        "body": (
            "## Les relectures sont assez fréquentes pour justifier un traçage\n\n"
            "**Les 8 340 colis routés uniques génèrent 9 529 événements de routage.** Au total, 882 colis (10,58 %) ont plus d'un événement, ce qui représente 1 189 événements supplémentaires; 416 colis sont vus sur plusieurs lignes ou sources et un colis atteint huit événements.\n\n"
            "Ces événements ne prouvent pas tous une recirculation anormale, mais leur concentration probable autour de la ligne 1 et des heures de pointe est une vérification opérationnelle prioritaire."
        ),
    },
    {
        "id": "destination_story",
        "type": "markdown",
        "sourceId": "src_destinations",
        "layout": "full",
        "body": (
            "## Quatre destinations représentent les deux tiers du flux routé\n\n"
            f"**St-Hubert, Montréal Gilmore, Québec et Blainville totalisent {top_four_destination_share:.1%} des colis routés uniques; les deux premiers seuls en représentent {top_two_destination_share:.1%}.** Cette concentration aide à cibler les routes et les chutes à vérifier pendant les trois vagues de charge.\n\n"
            "La destination est dérivée de l'expédition et du code postal parce que le champ de destination de l'événement convoyeur est vide. La dérivation couvre 8 333 colis sur 8 340; sept restent non renseignés."
        ),
    },
    {"id": "destination_chart_block", "type": "chart", "chartId": "chart_destinations", "layout": "full"},
    {
        "id": "chute_story",
        "type": "markdown",
        "sourceId": "src_chutes",
        "layout": "full",
        "body": (
            "## Le flux est réparti entre plusieurs chutes\n\n"
            f"**La combinaison la plus sollicitée, ligne 0 · chute 39, représente 601 événements (6,31 %).** Les cinq premières combinaisons totalisent {top_chute_events / routing['ROUTING_EVENTS']:.1%} des événements de routage. Aucune chute unique ne domine le flux, ce qui oriente l'enquête vers la ligne et l'équipement plutôt que vers un seul débouché."
        ),
    },
    {"id": "chute_table_block", "type": "table", "tableId": "table_chutes", "layout": "full"},
    {
        "id": "recommendations",
        "type": "markdown",
        "layout": "full",
        "body": (
            "## Actions recommandées\n\n"
            "1. **Prioriser la ligne 1** aux créneaux de 19 h, 22 h et minuit : lecture caméra, règles de rejet et synchronisation avec la chute.\n"
            "2. **Vérifier la balance de la ligne 3** : son taux d'erreur de poids est le plus élevé malgré un rejet presque nul.\n"
            "3. **Retracer un échantillon des 882 colis multi-événements**, en commençant par les 416 vus sur plusieurs lignes ou sources, pour distinguer recirculation normale, relecture et transfert.\n"
            "4. **Suivre les deux prochains dimanches** contre les repères pondérés : 5,19 % de rejet, 4,89 % de non-lecture et 4,13 % d'erreur de balance."
        ),
    },
    {
        "id": "questions",
        "type": "markdown",
        "layout": "full",
        "body": (
            "## Questions à approfondir\n\n"
            "- Les rejets de la ligne 1 sont-ils surtout des non-lectures, des rejets de chute ou des erreurs de synchronisation?\n"
            "- Les 416 colis vus sur plusieurs lignes suivent-ils un parcours attendu ou un transfert correctif?\n"
            "- Les erreurs de balance de la ligne 3 se concentrent-elles sur une plage de poids, un client ou un type d'emballage?"
        ),
    },
    {
        "id": "caveats",
        "type": "markdown",
        "layout": "full",
        "body": (
            "## Hypothèses et limites\n\n"
            "- La journée est la **date de tri opérationnelle** du 12 juillet, de dimanche 17 h jusqu'à la fermeture des lignes le lundi matin, en heure EDT.\n"
            "- Les rejets, non-lectures et erreurs techniques peuvent se chevaucher; ils ne doivent pas être additionnés pour calculer un taux de succès.\n"
            "- Les 10 350 passages officiels et les 8 340 colis routés uniques mesurent des grains différents; l'écart inclut non-lectures, rejets et relectures.\n"
            "- Les identifiants 0, 1 et 3 sont les identifiants techniques des lignes; les libellés métier historiques ne sont pas suffisamment fiables pour les renommer.\n"
            "- Les destinations utilisent la table de localisation actuelle; une modification récente de l'affectation d'un code postal pourrait déplacer un petit nombre de colis."
        ),
    },
]

artifact = {
    "surface": "report",
    "manifest": {
        "version": 1,
        "surface": "report",
        "title": "Convoyeur de St-Hubert — dimanche 12 juillet 2026",
        "description": "Analyse opérationnelle du volume, de la qualité, des lignes, des relectures et des destinations.",
        "generatedAt": generated_at,
        "blocks": blocks,
        "cards": cards,
        "charts": charts,
        "tables": tables,
        "sources": source_specs,
    },
    "snapshot": {
        "version": 1,
        "generatedAt": generated_at,
        "status": "ready",
        "datasets": {
            "headline_status": headline_status,
            "headline_hourly": headline_hourly,
            "hourly_main_lines": hourly_rows,
            "quality_comparison": quality_rows,
            "line_summary": line_rows,
            "routing_summary": routing_summary,
            "destination_depots": destination_rows,
            "top_destinations": top_destination_rows,
            "top_chutes": chute_rows,
            "scan_reconciliation": data["scan_reconciliation"],
            "manual_exceptions": data["manual_exceptions"],
        },
    },
    "sources": source_specs,
}

OUTPUT_DIR.mkdir(exist_ok=True)
ARTIFACT_PATH.write_text(
    json.dumps(artifact, ensure_ascii=False, indent=2),
    encoding="utf-8",
)

NOTES_PATH.write_text(
    """# Notes de construction du rapport

## Public et structure

- Public : parties prenantes opérationnelles et gestionnaires.
- Structure requise : titre, Executive Summary, constats avec preuves visuelles, actions, questions, hypothèses et limites.
- Surface : rapport HTML portable, choisi parce que le moteur MCP de rapport n'est pas callable dans cette session Desktop.

## Carte des visualisations

| Section | Question | Famille / type | Champs | Conclusion soutenue | Palette |
|---|---|---|---|---|---|
| Volume horaire | Quand la charge culmine-t-elle et quelle ligne contribue? | Composition / barres empilées | heure, ligne, scans | Pointe à minuit; charge répartie entre 3 lignes | catégorielle bleu-orange-rose |
| Qualité sur 5 dimanches | Les taux du 12 juillet sont-ils élevés? | Comparaison / barres groupées | date, défaut, taux, occurrences, passages | Trois taux au sommet de la fenêtre | catégorielle bleu-orange-rose |
| Destinations | Où vont les colis routés? | Classement / barres horizontales | dépôt, colis, part, cumul | Quatre destinations = 66,8 % | racine bleue |

## Contrôles

- Les 3 lignes totalisent 10 350 passages.
- L'historique brut totalise 10 330 lignes; écart officiel/brut = 20 (0,19 %).
- Les destinations totalisent 8 340 colis, comme la population routée dédupliquée.
- Les sept destinations non renseignées sont conservées dans la donnée, mais exclues du top 10.
- Le graphique de qualité utilise des taux fractionnels pour le format pourcentage.
""",
    encoding="utf-8",
)

print(ARTIFACT_PATH)
