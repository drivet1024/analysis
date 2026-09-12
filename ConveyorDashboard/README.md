# Dashboard d'analyse des convoyeurs

Application locale reliée à MySQL pour analyser les colis par convoyeur et repérer les clients associés aux problèmes opérationnels.

## Fonctions

- journée opérationnelle configurable;
- St-Hubert haut et sol séparés, Québec, Toronto et Gilmore;
- colis uniques, passages, recirculation, chute 98 et même chute répétée;
- taux sans poids et sans dimensions selon la règle « au moins un passage invalide »;
- distribution exclusive du dernier poids valide;
- débit horaire;
- détail des colis derrière chaque KPI;
- classement client avec taux sur le volume total et taux technique sur le convoyeur;
- signaux de très petit format, format atypique et poids faible;
- corrélation avec l'exception 25 sur sept jours complets;
- lecture OpenAI à la demande à partir d'agrégats anonymisés.

## Démarrage

1. Double-cliquer sur `start-dashboard.cmd`.
2. Si nécessaire, saisir la clé API OpenAI dans l'invite sécurisée.
3. Ouvrir `http://127.0.0.1:5077`.

Si le port 5077 est déjà utilisé par le dashboard, le script réutilise l'instance existante au lieu d'en démarrer une deuxième.

## Configuration

- MySQL : variables `MYSQL_HOST`, `MYSQL_PORT`, `MYSQL_DATABASE`, `MYSQL_USER`, `MYSQL_PASSWORD`.
- OpenAI : `OPENAI_API_KEY` et, facultativement, `OPENAI_MODEL`.
- Le fichier local `.env.local` n'est pas destiné au contrôle de source.
- Délai SQL par défaut : 300 secondes.
- Délai OpenAI : 5 minutes.

Voir [KPI_DEFINITIONS.md](KPI_DEFINITIONS.md) pour les règles exactes, les journées opérationnelles et l'index proposé.

## Prévisions EDI

La page `/edi-previsions.html` regroupe la prévision nationale EDI à sept jours et la comparaison des archives avec le réel. La page `/livraison-previsions.html` (LIVRAISON PRÉVISION) présente les prévisions de livraison par secteur, leurs filtres et leur méthode; elle charge directement `/api/edi/sectors`. L'EDI principal conserve les volumes du jour, la finale intrajournalière, les régions et les palettes; `/edi-clients.html` conserve le tableau client. Les liens entre ces pages conservent la date analysée.

### Secteurs Saint-Hubert

`/api/edi/sectors` prévoit les volumes affectés aux jours de livraison dans les secteurs `sector_info.DEPOTNUMBER=1`, sauf 500, 501, 539, 641 et 643. Ces exclusions sont appliquées aux réponses, y compris aux archives existantes sans les modifier; leurs volumes historiques sont classés hors périmètre pour conserver la réconciliation. Source : `parcel_history`, `EXCEPTION=903`, `DEPOT_ID=1`, `SOURCE_TYPE=200` avec `SOURCE_ID NULL/1/3` (haut et sol) ou `SOURCE_TYPE=201` (tri manuel), `VOID=0`, identifiants de colis valides. Horaire local existant : 15 h inclus à 3 h exclu. Lundi-jeudi soir → lendemain; vendredi ET dimanche soir → lundi; samedi soir exclu, aucune livraison le week-end.

Déduplication par `(jour de livraison, PARCEL_ID)` avant jointure, dernier passage pour les clés d’expédition. La jointure `SHIPPING_ID + EXP_DATE` conserve une unité par colis même si shipment contient plusieurs lignes : des secteurs contradictoires sont classés ambigus. Le code postal destination normalisé est prioritaire via location et sa route; repli `shipment.DEST_ROUTE_ID → route.SECTOR_ID`. La géographie actuelle est appliquée à l’historique. Le total source inclut les colis hors périmètre et non rattachés pour réconciliation. Il ne constitue pas un nombre de livraisons confirmées.

455 jours calendaires, cohortes strictement antérieures à la date de référence. Un lundi n’est utilisable que si vendredi et dimanche sont tous deux observés; les jours sans source restent inconnus. Le modèle utilise les mêmes jours de livraison, exclut les jours fériés et ceux alimentés par un tri férié, et aligne l’annuel sur les soirées commerciales (Cyber Monday → mardi; Black Friday + dimanche → lundi). Samedi et dimanche valent toujours zéro. L’absence de colis dans un secteur est zéro uniquement après son début d’activité et sur une journée source complète.

LIVRAISON PRÉVISION utilise une version hebdomadaire créée le samedi à 6 h (Montréal), figée pour le lundi au vendredi suivants. `sectors-st-hubert/weekly-saturday/yyyy-MM-dd-forecast.json` conserve cette version sans écrasement. Les anciennes archives quotidiennes `sorted-v2` et `postal-v1` restent conservées. Une semaine sans version créée le samedi présente des prévisions indisponibles, sans reconstitution rétroactive. Le serveur doit être disponible le samedi : après une interruption couvrant tout ce jour, il affiche le réel seul pour la semaine manquée.

### Palettes volumétriques par région

La colonne voisine des palettes à 60 colis utilise le mix client du jour (`shipment.PARCEL_NB`, mêmes régions et exclusions que les volumes existants). Les profils sont les moyennes de `parcel.LENGTH * WIDTH * HEIGHT`, par client, sur les 28 jours complets précédents, avec dimensions strictement positives. Minimum 20 mesures par client; sinon moyenne globale pondérée par le nombre de colis dimensionnés. Les nombres de colis avec profil client, avec remplacement global et sans profil sont exposés par région. Sans profil utilisable pour une partie des colis, l'estimation régionale reste indisponible.

Confirmé par l'utilisateur : dimensions source en pouces et palette de 40 × 48 × 84 po, base incluse dans les 84 po. Réglages estimés : hauteur utile 78 po (réserve de 6 po pour la base), remplissage 70 %. L'unité est verrouillée en pouces; les contrôles permettent une réserve de base de 4, 6 ou 8 po et un remplissage de 60 à 80 %, mémorisés dans le navigateur. Les anciennes sélections en centimètres ou sans réserve de base sont ignorées. Le calcul arrondit chaque région à l'entier supérieur avant de sommer. C'est un modèle volumétrique, sans placement géométrique ni contraintes de poids, de fragilité ou de tournées. Vérifications : `node tests/EdiForecastChecks/pallet-render-checks.mjs`.

La ligne des colis du jour inclut une « Finale estimée » actualisée toutes les 60 secondes. Elle divise le volume déjà créé par la proportion pondérée reçue à la même heure pour les mêmes jours de semaine des huit semaines précédentes (journées de 4 h à 4 h). Les poids vont de 1 à N du plus ancien au plus récent; le numérateur et le dénominateur de la proportion utilisent les mêmes poids. Minimum quatre journées valides, une heure écoulée, des colis créés et une proportion historique d'au moins 5 %. Les jours fériés et la période Cyber Monday ne sont pas assimilés aux journées ordinaires. Le détail expose les références, leurs volumes partiels et finaux et les poids. Les dates terminées affichent le total observé. Cette projection dynamique n'écrase pas les prévisions à sept jours archivées.

La page EDI prévoit les sept jours suivant la date de référence à partir du total `parcel`, hors statuts 500/501, par journée de 4 h à 4 h. Elle utilise les huit semaines précédentes, compare les mêmes jours de semaine et pondère les observations disponibles de 1 à N (la plus récente reçoit le plus de poids). Il faut au moins quatre observations; les jours sans lignes source restent inconnus. La journée de référence et les dates suivantes sont exclues du calcul.

Les jours fériés du Québec sont exclus des références et de l'évaluation rétrospective, avec exclusion prudente du Vendredi saint et du lundi de Pâques. Le calendrier est défini dans `EdiHolidayCalendar.cs`; les congés propres à l'entreprise et reports additionnels se configurent via `EDI_EXTRA_CLOSURES=2026-12-28,2027-01-04`. Un jour férié futur n'est pas assimilé à une journée ordinaire : sa prévision est indisponible. Les promotions et changements de clients ne sont pas modélisés.

Chaque prévision expose les dates, volumes, poids et extrêmes historiques utilisés. Le total est affiché uniquement si les sept jours sont calculables. Une lecture agrégée de 84 jours permet aussi quatre tests rétrospectifs sur des horizons de sept jours : erreur absolue moyenne en colis et erreur absolue cumulée rapportée au volume réel cumulé. Les statuts actuels et corrections tardives limitent cette reconstitution.

Le modèle v3 lit 455 journées une fois au renouvellement pour intégrer les références annuelles; la consultation des prévisions archivées conserve la lecture récente de 84 jours pour le réel. Pour les jours ordinaires, il combine 50 % de moyenne récente et 50 % de moyenne annuelle ajustée (même jour de semaine à J−364 et ±7 jours, minimum deux observations). Le facteur d'activité est le rapport des sommes sur les jours appariés des 56 jours précédents et leurs homologues à J−364, avec au moins 21 paires et un dénominateur positif. Fériés et période Cyber Monday sont exclus des deux côtés. Le partage 50/50 est fixé avant le test, pas optimisé sur ses résultats.

Du lundi précédant le Cyber Monday au deuxième dimanche suivant (J−7 à J+13), la référence est la même position par rapport au Cyber Monday N−1, multipliée par le facteur d'activité. Le Cyber Monday suit le quatrième jeudi de novembre; ce repérage gère aussi les décalages de 53 semaines. Aucun mélange avec les lundis ordinaires ne vient atténuer le pic. Cette période est exclue de la tendance ordinaire et du calcul du facteur d'activité. Si la référence annuelle événementielle manque, la prévision reste indisponible; ailleurs, l'absence d'historique annuel entraîne un retour à la tendance récente. Le min.–max. affiché concerne les références récentes, pas un intervalle prédictif.

La page expose les références annuelles, le facteur d'activité et ses paires sources, le dernier volume Cyber Monday disponible, ainsi que l'erreur du modèle v3 et du modèle v2 évalués sur exactement les mêmes jours des quatre semaines rétrospectives. Cette évaluation récente ne garantit pas la précision du pic commercial. Les archives v1 et v2 restent intactes; v3 utilise de nouveaux identifiants de fichiers.

Le service `EdiForecastRefreshService` vérifie chaque minute si le renouvellement de 6 h, fuseau `America/Toronto`, est dû. À 6 h il calcule demain à J+7; avant 6 h la version de la veille reste active. Le serveur doit rester en fonctionnement. Au redémarrage il rattrape la dernière échéance manquée avec l'horodatage réel, sans fabriquer les versions des jours d'arrêt. En cas d'erreur, il conserve la version précédente et réessaie dans une minute.

Les archives JSON sont immuables dans `ConveyorDashboard/App_Data/edi-forecasts` (ou `EDI_FORECAST_PATH`). Chaque version est écrite dans un fichier temporaire puis renommée atomiquement avant d'être affichée; le fichier précédent n'est jamais remplacé. Ne pas effacer ce dossier lors d'une mise à jour; l'inclure dans les sauvegardes. Compose monte ce dossier depuis l'hôte et règle le fuseau du conteneur. Il est exclu de Git et de l'image Docker.

« Prévu et réel » affiche les 30 dernières versions, filtrables, et compare uniquement les journées terminées de l'historique chargé. Chaque version garde son horizon J+1 à J+7. Une prévision enregistrée après le début du jour prévu n'est pas évaluée. Le réel peut évoluer lors de corrections de la source; les prévisions ne changent pas. Pour une date passée sans archive, l'écran indique explicitement une reconstitution non archivée.

Vérifications du calcul : `dotnet run --project tests/EdiForecastChecks` depuis la racine du dépôt. Redémarrer le serveur après compilation pour activer le nouveau champ `forecast` de `/api/edi`.

Dans LIVRAISON PRÉVISION, chaque secteur affiche aussi `sector_info.SECTOR_CONTACT` et le nombre de routes distinctes rattachées au secteur ayant au moins une expédition avec `PARCEL_NB>0` sur les 28 derniers jours complets (`shipment.INSERT_DATE`, hors statuts 500/501, destination `DEST_ROUTE_ID`). Ces métadonnées actuelles sont enrichies avec le relevé quotidien, même pour une ancienne prévision, sans réécrire les archives. Le nombre de codes postaux est conservé.
Le réel utilise exactement les mêmes colis uniques triés et le même regroupement par date de livraison que la prévision. À 6 h chaque jour, les cohortes terminées (y compris celle du jour, tri clos à 3 h) sont relues pour la semaine; les corrections tardives peuvent ajuster le réel. Les relevés sont conservés dans `weekly-saturday/actuals/samedi-date_du_relevé.json`. Les dates futures et journées sources incomplètes restent indisponibles, jamais converties à zéro. Le navigateur programme une seule actualisation à la prochaine échéance quotidienne, sans interrogation chaque minute; le bouton Actualiser relit le relevé sauvegardé, sans recomputation dans la journée. Les tests de persistance et de calendrier incluent dimanche, lundi avant/après 6 h, mardi, redémarrage, corrections et semaine manquée.

EDI PRÉVISION charge exclusivement `/api/edi/forecasts` : lecture des archives et du réel sauvegardé, aucune requête MySQL et aucun recalcul depuis cette page. Le traitement de fond crée la prévision à 6 h et enregistre le réel des 84 derniers jours complets dans `edi-forecasts/actuals/yyyy-MM-dd.json` une seule fois par jour. Le navigateur recharge à la prochaine échéance de 6 h; si le traitement de fond est encore en cours ou a échoué, il relit seulement les archives après une minute. Sans archive pour une date, la prévision reste indisponible; aucune reconstitution ne ralentit l’ouverture.

Le tableau EDI PRÉVISION réunit désormais les colonnes prévu, réel et écart (réel − prévu), avec les explications du même instantané. Le sélecteur de version charge une archive précise via `/api/edi/forecasts?version=...`, sans accès MySQL ni recalcul. Les écarts ne sont affichés que pour les journées évaluables de cette version; les totaux réels restent incomplets tant que les sept journées ne sont pas disponibles.
