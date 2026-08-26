# Notes de construction du rapport

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
