from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / ".tools" / "python-packages"))
import nbformat as nbf


output_dir = Path(__file__).resolve().parent
output_path = output_dir / "conveyor_st_hubert_2026-07-12.ipynb"

notebook = nbf.v4.new_notebook()
notebook["metadata"] = {
    "kernelspec": {
        "display_name": "Python 3",
        "language": "python",
        "name": "python3",
    },
    "language_info": {"name": "python", "version": "3"},
}

notebook["cells"] = [
    nbf.v4.new_markdown_cell(
        """# Convoyeur de St-Hubert — dimanche 12 juillet 2026

## tl;dr

- Les compteurs de tri enregistrent **10 350 passages**, soit **+18,5 %** par rapport à la moyenne des quatre dimanches précédents et **+31,6 %** par rapport au 5 juillet.
- La charge est concentrée sur la **ligne 1 (46,5 % du volume)** et le débit brut atteint son maximum entre **00 h et 00 h 59 avec 1 999 scans**.
- Les taux de **rejet (7,25 %)**, de **non-lecture (6,33 %)** et d'**erreur de balance (6,07 %)** sont les plus élevés des cinq dimanches observés.
- La ligne 1 explique **74,9 % des rejets** et **68,5 % des non-lectures**; c'est la priorité d'enquête.
- **8 340 colis uniques** ont reçu un routage réussi. Parmi eux, **882 (10,58 %)** ont plus d'un événement de routage, pour **1 189 événements supplémentaires**.
"""
    ),
    nbf.v4.new_markdown_cell(
        """## Context & Methods

Analyse en lecture seule du dépôt `ST-HUBERT` (`DEPOT_ID = 1`). La journée opérationnelle du dimanche est définie par les compteurs `conveyor_status.sort_date = 2026-07-12`. Les scans bruts commencent vers 17 h le dimanche; les lignes 0 et 1 ferment à 2 h 59 le lundi et la ligne 3 à 7 h 59.

La comparaison porte sur les quatre dimanches précédents (14, 21 et 28 juin; 5 juillet). Les taux comparatifs sont pondérés par le nombre de passages.

### Key Assumptions

- `conveyor_status.nb_parcel` est le compteur officiel de passages pour la journée de tri.
- `parcel_scan_history` sert à décrire la forme horaire du flux; `parcel_history` avec `SOURCE_TYPE = 200` sert à dédupliquer les colis routés.
- Les rejets, non-lectures et erreurs techniques peuvent se chevaucher; leurs nombres ne doivent pas être additionnés pour produire un taux de succès.
- Les dates sont interprétées en heure locale du serveur, confirmée en EDT.
- Les résultats exportés sont agrégés; aucun code-barres, nom de client ni adresse n'est inclus.
"""
    ),
    nbf.v4.new_markdown_cell("## Data"),
    nbf.v4.new_code_cell(
        """from pathlib import Path
import json
from statistics import mean

results_path = Path.cwd() / "conveyor_st_hubert_results_2026-07-12.json"
payload = json.loads(results_path.read_text(encoding="utf-8"))
data = payload["results"]

assert payload["source"]["database"] == "nationex"
assert payload["source"]["depot_id"] == 1
assert payload["source"]["sorting_date"] == "2026-07-12"
payload["source"]
"""
    ),
    nbf.v4.new_markdown_cell("## Results\n\n### 1. Volume et qualité par dimanche"),
    nbf.v4.new_code_cell(
        """sundays = data["sunday_totals"]
current = sundays[-1]
prior = sundays[:-1]

prior_parcels = sum(row["PARCELS"] for row in prior)
current_metrics = {
    "passages": current["PARCELS"],
    "vs_moyenne_4_dimanches_pct": round(
        100 * (current["PARCELS"] / (prior_parcels / len(prior)) - 1), 2
    ),
    "vs_5_juillet_pct": round(
        100 * (current["PARCELS"] / prior[-1]["PARCELS"] - 1), 2
    ),
    "rejets_pct": current["REJECT_RATE_PCT"],
    "rejets_vs_4_dimanches_pp": round(
        current["REJECT_RATE_PCT"]
        - 100 * sum(row["REJECTED"] for row in prior) / prior_parcels,
        2,
    ),
    "non_lectures_pct": current["NOREAD_RATE_PCT"],
    "non_lectures_vs_4_dimanches_pp": round(
        current["NOREAD_RATE_PCT"]
        - 100 * sum(row["NOREAD"] for row in prior) / prior_parcels,
        2,
    ),
    "erreurs_balance_pct": current["SCALE_ERROR_RATE_PCT"],
    "erreurs_balance_vs_4_dimanches_pp": round(
        current["SCALE_ERROR_RATE_PCT"]
        - 100 * sum(row["SCALE_ERRORS"] for row in prior) / prior_parcels,
        2,
    ),
}
current_metrics
"""
    ),
    nbf.v4.new_markdown_cell("### 2. Profil horaire et répartition des lignes"),
    nbf.v4.new_code_cell(
        """hourly = data["hourly_main_lines"]
hour_totals = {}
for row in hourly:
    hour = row["EVENT_HOUR"]
    hour_totals.setdefault(hour, 0)
    hour_totals[hour] += row["SCAN_ROWS"]

peak_hour, peak_scans = max(hour_totals.items(), key=lambda item: item[1])
line_summary = data["line_summary"]
total_passages = sum(row["nb_parcel"] for row in line_summary)
line_results = [
    {
        "ligne": row["line_id"],
        "part_volume_pct": round(100 * row["nb_parcel"] / total_passages, 2),
        "part_rejets_pct": round(100 * row["nb_rejected"] / current["REJECTED"], 2),
        "part_non_lectures_pct": round(100 * row["nb_noread"] / current["NOREAD"], 2),
        "taux_rejet_pct": row["REJECT_RATE_PCT"],
        "taux_non_lecture_pct": row["NOREAD_RATE_PCT"],
        "taux_erreur_balance_pct": row["SCALE_ERROR_RATE_PCT"],
    }
    for row in line_summary
]

{"heure_de_pointe": peak_hour, "scans_de_pointe": peak_scans, "lignes": line_results}
"""
    ),
    nbf.v4.new_markdown_cell("### 3. Déduplication, routage et destinations"),
    nbf.v4.new_code_cell(
        """reconciliation = next(
    row for row in data["scan_reconciliation"] if row["LINE_ID"] == "TOTAL"
)
routing = data["routing_events"][0]
destinations = data["destination_depots"]
destination_total = sum(row["UNIQUE_PARCELS"] for row in destinations)
top_four_destinations = sum(row["UNIQUE_PARCELS"] for row in destinations[:4])

routing_results = {
    "official_vs_raw_difference": current["PARCELS"] - reconciliation["RAW_SCAN_ROWS"],
    "official_vs_raw_difference_pct": round(
        100 * (current["PARCELS"] - reconciliation["RAW_SCAN_ROWS"]) / current["PARCELS"], 2
    ),
    "unique_routed_parcels": routing["UNIQUE_ROUTED_PARCELS"],
    "parcels_with_multiple_routing_events": routing["PARCELS_WITH_MULTIPLE_ROUTING_EVENTS"],
    "multi_event_rate_pct": routing["MULTI_EVENT_PARCEL_RATE_PCT"],
    "extra_routing_events": routing["EXTRA_ROUTING_EVENTS"],
    "parcels_seen_on_multiple_lines": routing["PARCELS_SEEN_ON_MULTIPLE_LINES_OR_SOURCES"],
    "top_four_destination_share_pct": round(100 * top_four_destinations / destination_total, 2),
    "missing_destination_parcels": next(
        row["UNIQUE_PARCELS"]
        for row in destinations
        if row["DESTINATION_DEPOT"] == "NON RENSEIGNÉ"
    ),
}
routing_results
"""
    ),
    nbf.v4.new_markdown_cell("### 4. Reasonableness checks"),
    nbf.v4.new_code_cell(
        """assert total_passages == current["PARCELS"] == 10350
assert reconciliation["RAW_SCAN_ROWS"] == 10330
assert peak_scans == 1999
assert routing["UNIQUE_ROUTED_PARCELS"] == 8340
assert destination_total == routing["UNIQUE_ROUTED_PARCELS"]
assert routing["EXTRA_ROUTING_EVENTS"] == 1189
assert current["REJECT_RATE_PCT"] == max(row["REJECT_RATE_PCT"] for row in sundays)
assert current["NOREAD_RATE_PCT"] == max(row["NOREAD_RATE_PCT"] for row in sundays)
assert current["SCALE_ERROR_RATE_PCT"] == max(row["SCALE_ERROR_RATE_PCT"] for row in sundays)

"Tous les contrôles de cohérence attendus sont satisfaits."
"""
    ),
    nbf.v4.new_markdown_cell(
        """## Takeaways

1. **Le volume était élevé, mais pas exceptionnel.** Le 12 juillet dépasse nettement le 5 juillet et la moyenne des quatre dimanches précédents, tout en restant sous le pic du 28 juin.
2. **La ligne 1 est le principal point d'intervention.** Elle transporte 46,5 % du volume, mais concentre 74,9 % des rejets et 68,5 % des non-lectures.
3. **Le problème n'est pas limité à la lecture.** Les erreurs de balance atteignent aussi un sommet de cinq semaines; la ligne 3 mérite une vérification spécifique avec 9,83 % d'erreurs de balance malgré presque aucun rejet.
4. **Les relectures sont significatives.** 10,58 % des colis routés ont plus d'un événement et 416 colis sont vus sur plus d'une ligne ou source, ce qui suggère de regarder la recirculation et les transferts entre lignes.
5. **La desserte est concentrée.** St-Hubert, Montréal Gilmore, Québec et Blainville représentent ensemble 66,8 % des colis routés. La jointure par code postal couvre 8 333 colis sur 8 340; sept destinations restent non renseignées.
6. **Priorité proposée.** Examiner d'abord la ligne 1 durant les créneaux 19 h, 22 h et minuit, puis vérifier la balance de la ligne 3 et retracer un échantillon des colis multi-scannés.
"""
    ),
]

nbf.write(notebook, output_path)
print(output_path)
