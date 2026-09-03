# Graphiques horaires du convoyeur

| Section | Question | Forme | Champs | Définition | Palette | Source |
|---|---|---|---|---|---|---|
| Capacité du haut | Quand le convoyeur du haut fonctionne-t-il sous sa capacité pratique? | Barres de 15 minutes annualisées en colis/heure et ligne de référence | `parcelsPerHour`, `utilizationPercent`, `status` | Capacité pratique = 75e percentile de la meilleure fenêtre continue de 60 minutes de chaque quart complété sur les 14 derniers jours | Vert / ambre / rouge | `parcel_history` sur le serveur 101 |
| Creux du haut | Quelles périodes soutenues demandent une vérification? | Tableau de périodes | `start`, `end`, `durationMinutes`, `averagePerHour`, `utilizationPercent` | Blocs consécutifs de 5 minutes sous 40 % de la capacité pratique, à partir du premier colis du quart | Rouge | Agrégation du graphique de capacité |
| Convoyeur du haut | Combien de colis uniques passent chaque heure après 16 h? | Barres horaires | `hour`, `parcels` | Heure du premier passage du colis sur `SOURCE_TYPE=200`, `SOURCE_ID NULL/1` | Bleu | `parcel_history` sur le serveur 101 |
| Convoyeur du bas | Combien de colis uniques passent chaque heure après 16 h? | Barres horaires | `hour`, `parcels` | Heure du premier passage du colis sur `SOURCE_TYPE=200`, `SOURCE_ID=3` | Vert | `parcel_history` sur le serveur 101 |
| Scan manuel | Combien de colis uniques sont scannés chaque heure après 16 h? | Barres horaires | `hour`, `parcels` | Heure du premier passage du colis sur `SOURCE_TYPE=201` | Ambre | `parcel_history` sur le serveur 101 |

Les trois graphiques couvrent le quart opérationnel de 16 h à 3 h 59 le lendemain, commencent à zéro et partagent la même échelle verticale à chaque actualisation.
