# Définitions des KPI convoyeurs

## Source et grain

- Source : `parcel_history`.
- Événement convoyeur automatisé : `EXCEPTION = 903` et `SOURCE_TYPE = 200`.
- Grain analytique : un `PARCEL_ID` par convoyeur et par journée opérationnelle.
- Événements exclus : `PARCEL_ID` NULL/0, `VOID = 1`, sources non cartographiées et postes manuels 201xx.
- Temps métier : `DATE_LIV`. Un filtre élargi sur `DATE_INSERT` sert uniquement à l'élagage des partitions.

## Cartographie et journées opérationnelles

| Convoyeur | Dépôt | Source | Fenêtre |
|---|---:|---|---|
| St-Hubert — haut | 1 | `SOURCE_ID IS NULL OR SOURCE_ID = 1` | 15:00 à 03:00 |
| St-Hubert — sol | 1 | `SOURCE_ID = 3` | 15:00 à 03:00 |
| Québec | 2 | `SOURCE_ID IS NULL` | 13:00 à 07:00 |
| Toronto | 12 | `SOURCE_ID IS NULL` | 15:00 à 09:00 |
| Gilmore | 28 | `SOURCE_ID IS NULL` | 15:00 à 09:00 |

La valeur historique « ligne 0 » est donc représentée par `SOURCE_ID IS NULL`, et non par `SOURCE_ID = 0`.

## Indicateurs

- **Colis uniques** : nombre de `PARCEL_ID` distincts.
- **Passages** : nombre de lignes `parcel_history` admissibles.
- **Recirculé** : colis ayant plus d'un passage sur le même convoyeur dans la même journée opérationnelle.
- **Chute 98** : colis ayant au moins un passage avec `CHUTE_NO = 98`.
- **Même chute répétée** : une chute différente de 98 apparaît au moins deux fois pour le colis.
- **Heure de répétition** : heure du deuxième passage vers une même chute non-98. Si plusieurs chutes sont répétées pour un colis, seule la première répétition chronologique est retenue; chaque colis appartient donc à une seule heure.
- **Sans poids** : au moins un passage du colis a `WEIGHT IS NULL OR WEIGHT <= 0`. Le colis compte une seule fois dans le KPI.
- **Sans dimensions** : au moins un passage a une longueur, largeur ou hauteur NULL ou non positive. Le colis compte une seule fois dans le KPI.
- **Catégories de poids** : catégories exclusives basées sur le dernier poids valide du colis : `< 1`, `1 à 3`, `> 3 à 5`, `> 5 à 10`, `> 10 lb`.
- **Gilmore** : poids, dimensions et catégories de poids sont non applicables. Les colis Gilmore sont exclus des numérateurs et dénominateurs de ces KPI.

Les indicateurs de problème ne sont pas mutuellement exclusifs. Un colis peut être recirculé et avoir un passage sans poids.

## Analyse client

- L'identité client vient d'abord de `parcel_history.CUSTOMER_ID`, avec repli sur `parcel.CUSTOMER_ID` par `PARCEL_ID`.
- Le taux technique utilise les colis convoyeur du client comme dénominateur.
- Le taux d'impact global utilise tous les colis du client pour les dates `EXP_DATE` représentées parmi ses colis convoyeur de la journée.
- Le volume total est réconcilié pour ne jamais être inférieur au sous-ensemble des colis convoyeur.
- Très petit format : volume valide inférieur à 96 unités³. C'est un seuil exploratoire.
- Format atypique : rapport entre le plus grand et le plus petit côté supérieur ou égal à 5. C'est un signal de vérification, pas une preuve de cause.

## Exception 25

La corrélation utilise sept journées opérationnelles complètes. Elle compare le taux de problème convoyeur des colis avec exception 25 au taux des autres colis.

- Lien fort : ratio ≥ 2 et différence ≥ 10 points de pourcentage.
- Lien modéré : ratio ≥ 1,5 et différence ≥ 5 points.
- Données insuffisantes : moins de 100 colis dans l'un des deux groupes.

Une association ne démontre pas une causalité.

## Index proposé

À évaluer avec `EXPLAIN` avant toute création :

```sql
CREATE INDEX IX_parcel_history_conveyor_analysis
ON parcel_history (EXCEPTION, SOURCE_TYPE, DEPOT_ID, SOURCE_ID, DATE_LIV, PARCEL_ID);
```

Le dashboard ne crée aucun index automatiquement.
