from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / ".tools" / "python-packages"))
import nbformat as nbf


output_dir = Path(__file__).resolve().parent
output_path = output_dir / "nationex_integrity_check.ipynb"

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
        """# Nationex — contrôle d’intégrité du modèle colis

## tl;dr

- Sur le 9 juillet 2026, les **33 022 colis** retrouvent tous leur `shipment` courant par `(SHIPPING_ID, EXP_DATE)` et leur `customer`.
- `SHIPMENT_INTERNAL_ID` vaut `0` sur toute cette fenêtre; il ne doit pas servir de clé de jointure.
- **7 668 colis (23,22 %)** ne retrouvent pas `shipping`, ce qui confirme que cette table est un chemin legacy plutôt que le parent courant.
- NAT_CLIK API produit **33 676 événements** : 100 % existent dans `livraison`, tandis que 97,84 % existent aussi dans le modèle courant.
- **9 801 événements sur 314 395 (3,12 %)** utilisent un code source absent du référentiel.
"""
    ),
    nbf.v4.new_markdown_cell(
        """## Context & Methods

Contrôle diagnostique en lecture seule sur `natdev02 / nationex`, limité à une journée complète. Les plans ont été vérifiés avant exécution : partition `p2026`, lecture par plage sur les dates et recherches parentales indexées.

### Key Assumptions

- Le grain courant est un colis dans `parcel`, une expédition dans `shipment` et un événement dans `parcel_history`.
- La clé métier de `parcel → shipment` est `(SHIPPING_ID, EXP_DATE)`.
- Le fichier de résultats contient uniquement des agrégats; aucune ligne client, adresse ou information personnelle n’est exportée.
- L’échantillon `parcel_history` correspond aux 5 000 premiers événements ordonnés de la journée et ne doit pas être traité comme un échantillon aléatoire.

Sources reproductibles : `nationex_integrity_queries.sql` et `../MySqlTool/Program.cs`.
"""
    ),
    nbf.v4.new_markdown_cell("## Data"),
    nbf.v4.new_code_cell(
        """from pathlib import Path
import json

results_path = Path.cwd() / "nationex_integrity_results_2026-07-09.json"
data = json.loads(results_path.read_text(encoding="utf-8"))

assert data["source"]["database"] == "nationex"
assert data["source"]["extraction"].startswith("Aggregates only")
data["source"]
"""
    ),
    nbf.v4.new_markdown_cell("## Results\n\n### 1. Relations du modèle courant"),
    nbf.v4.new_code_cell(
        """def pct(numerator, denominator):
    return round(100 * numerator / denominator, 4) if denominator else 0.0

parcel = data["parcel"]
shipment = data["shipment"]

core_results = {
    "parcel_total": parcel["total"],
    "parcel_without_current_shipment": parcel["without_current_shipment"],
    "parcel_without_customer": parcel["without_customer"],
    "parcel_without_legacy_shipping": parcel["without_legacy_shipping"],
    "legacy_shipping_missing_pct": pct(parcel["without_legacy_shipping"], parcel["total"]),
    "shipment_total": shipment["total"],
    "shipment_without_customer": shipment["without_customer"],
}
core_results
"""
    ),
    nbf.v4.new_markdown_cell("### 2. NAT_CLIK et coexistence legacy"),
    nbf.v4.new_code_cell(
        """natclik = data["natclik_full_day"]
natclik_results = {
    "events": natclik["total_events"],
    "current_model_coverage_pct": pct(natclik["matched_current_parcel"], natclik["total_events"]),
    "legacy_livraison_coverage_pct": pct(natclik["matched_legacy_livraison"], natclik["total_events"]),
    "legacy_only_events": natclik["total_events"] - natclik["matched_current_parcel"],
    "without_customer": natclik["without_customer"],
}
natclik_results
"""
    ),
    nbf.v4.new_markdown_cell("### 3. Couverture du référentiel des sources"),
    nbf.v4.new_code_cell(
        """source_types = data["source_types"]
total_events = sum(item["events"] for item in source_types)
undocumented = [item for item in source_types if not item["documented"]]
undocumented_events = sum(item["events"] for item in undocumented)

reference_results = {
    "total_events": total_events,
    "undocumented_codes": len(undocumented),
    "undocumented_events": undocumented_events,
    "undocumented_event_pct": pct(undocumented_events, total_events),
    "codes": [item["code"] for item in undocumented],
}
reference_results
"""
    ),
    nbf.v4.new_markdown_cell("### 4. Reasonableness checks"),
    nbf.v4.new_code_cell(
        """assert parcel["shipment_internal_id_zero"] == parcel["total"]
assert parcel["without_current_shipment"] == 0
assert parcel["without_customer"] == 0
assert shipment["without_customer"] == 0
assert natclik["matched_legacy_livraison"] == natclik["total_events"]
assert natclik["without_customer"] == 0
assert total_events == 314395
assert undocumented_events == 9801

"Tous les contrôles de cohérence attendus sont satisfaits."
"""
    ),
    nbf.v4.new_markdown_cell(
        """## Takeaways

1. **Le modèle courant est cohérent sur la fenêtre testée.** Aucun colis ou shipment récent n’est orphelin de son parent principal.
2. **La clé `SHIPMENT_INTERNAL_ID` est trompeuse pour les données récentes.** Les jointures analytiques doivent utiliser `(SHIPPING_ID, EXP_DATE)`.
3. **NAT_CLIK est un flux hybride.** `livraison` reste la couverture complète; 729 événements de la journée sont uniquement dans le chemin legacy au moment de la mesure.
4. **Le référentiel `parcel_history_source_type` est incomplet.** Onze codes, représentant 3,12 % des événements de la journée, doivent être documentés pour fiabiliser les rapports par source.
5. **Portée limitée.** Ces constats couvrent le 9 juillet 2026; une surveillance automatisée devrait comparer plusieurs jours avant d’établir un seuil permanent.
"""
    ),
]

nbf.write(notebook, output_path)
print(output_path)
