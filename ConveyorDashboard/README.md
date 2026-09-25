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

La page `/edi-previsions.html` regroupe la prévision nationale EDI hebdomadaire figée du samedi au vendredi et la comparaison des archives avec le réel. La page `/livraison-previsions.html` (LIVRAISON PRÉVISION) présente les prévisions de livraison par secteur, leurs filtres et leur méthode; elle charge directement `/api/edi/sectors`. L'EDI principal conserve les volumes du jour et la finale intrajournalière. La page `/edi-transport.html` (EDI TRANSPORT) regroupe le tableau des colis et palettes par région avec ses réglages de hauteur utile et de remplissage; `/edi-clients.html` conserve le tableau client. Les liens entre ces pages conservent la date analysée.

### Comparaison ML.NET LightGBM pour l’EDI national

Les archives EDI v5 enregistrent côte à côte la prévision statistique et une prévision de régression LightGBM exécutée localement par ML.NET. Aucun service infonuagique, appel OpenAI ou transfert de données n'est utilisé pour ce calcul. Le modèle LightGBM est un modèle concurrent : il ne remplace pas la prévision statistique et ses résultats restent identifiables séparément dans le tableau, le graphique et les écarts au réel.

L'apprentissage consulte 1 120 jours calendaires et exige au moins 365 journées utilisables après la construction des variables. Cette lecture longue a lieu seulement au premier entraînement et le samedi; les autres renouvellements quotidiens lisent 455 jours pour construire les variables des sept prévisions avec le modèle sauvegardé. Une ligne représente une journée complète. Ses variables sont limitées aux renseignements antérieurs : cycles du jour de semaine et de l'année, volumes à J−7/J−14/J−21/J−28, moyennes des quatre et huit dernières occurrences du même jour, tendance récente, référence N−1, facteur de croissance annuel et position autour du Cyber Monday. Les jours fériés sont exclus comme cibles et comme références récentes ordinaires; la période Cyber Monday possède ses variables propres. Les sept dates prévues utilisent toutes des retards d'au moins sept jours, de sorte qu'aucun volume postérieur à l'heure de création n'est requis.

Le premier démarrage entraîne un modèle si aucun modèle valide n'existe. Ensuite, le renouvellement du samedi entraîne une nouvelle version et produit les sept prédictions figées du samedi au vendredi; aucun nouvel instantané de prévision n'est créé pendant la semaine. En cas d'échec d'entraînement ou de chargement, la prévision statistique continue d'être archivée et affichée. LightGBM utilise au plus deux fils d'exécution afin de préserver les ressources du tableau de bord.

La qualité affichée est mesurée sur les 56 dernières journées utilisables, conservées à la fin de la chronologie et exclues de l'apprentissage du modèle évalué. MAE et WAPE sont calculés sur ce bloc; le modèle final est ensuite entraîné sur toutes les journées disponibles. Les modèles `.zip` et leurs métadonnées JSON sont écrits sans écrasement dans `/app/App_Data/ml-models/edi` (`EDI_ML_MODEL_PATH`). Le montage persistant de Compose protège ces fichiers pendant les déploiements au même titre que les archives de prévisions.

### Secteurs Saint-Hubert

`/api/edi/sectors` prévoit les volumes affectés aux jours de livraison dans les secteurs `sector_info.DEPOTNUMBER=1`, sauf 500, 501, 539, 641 et 643. Ces exclusions sont appliquées aux réponses, y compris aux archives existantes sans les modifier; leurs volumes historiques sont classés hors périmètre pour conserver la réconciliation. Source : `parcel_history`, `EXCEPTION=903`, `DEPOT_ID=1`, `SOURCE_TYPE=200` avec `SOURCE_ID NULL/1/3` (haut et sol) ou `SOURCE_TYPE IN (201,202,204,205)` (NCV, petits colis, autres/incomplets et NCV poids manuel), `VOID=0`, identifiants de colis valides. Horaire local existant : 15 h inclus à 3 h exclu. Lundi-jeudi soir → lendemain; vendredi ET dimanche soir → lundi; samedi soir exclu, aucune livraison le week-end.

Déduplication par `(jour de livraison, PARCEL_ID)` avant jointure, dernier passage pour les clés d’expédition. La jointure `SHIPPING_ID + EXP_DATE` conserve une unité par colis même si shipment contient plusieurs lignes : des secteurs contradictoires sont classés ambigus. Le code postal destination normalisé est prioritaire via location et sa route; repli `shipment.DEST_ROUTE_ID → route.SECTOR_ID`. La géographie actuelle est appliquée à l’historique. Le total source inclut les colis hors périmètre et non rattachés pour réconciliation. Il ne constitue pas un nombre de livraisons confirmées.

455 jours calendaires, cohortes strictement antérieures à la date de référence. Un lundi n’est utilisable que si vendredi et dimanche sont tous deux observés; les jours sans source restent inconnus. Le modèle utilise les mêmes jours de livraison, exclut les jours fériés et ceux alimentés par un tri férié, et aligne l’annuel sur les soirées commerciales (Cyber Monday → mardi; Black Friday + dimanche → lundi). Samedi et dimanche valent toujours zéro. L’absence de colis dans un secteur est zéro uniquement après son début d’activité et sur une journée source complète.

LIVRAISON PRÉVISION utilise une version hebdomadaire créée le samedi à 6 h (Montréal), figée pour le lundi au vendredi suivants. `sectors-st-hubert/weekly-saturday/yyyy-MM-dd-forecast.json` conserve cette version sans écrasement. Les nouvelles archives quotidiennes `sorted-v3` incluent les types 200, 201, 202, 204 et 205 dans l’apprentissage et les nouveaux relevés du réel, avec déduplication commune par colis et jour de livraison. Les anciennes archives quotidiennes `sorted-v2` et `postal-v1` restent conservées, comme les prévisions hebdomadaires déjà figées; les relevés du réel déjà enregistrés sont conservés et le prochain relevé quotidien utilise le périmètre élargi. Une semaine sans version créée le samedi présente des prévisions indisponibles, sans reconstitution rétroactive. Le serveur doit être disponible le samedi : après une interruption couvrant tout ce jour, il affiche le réel seul pour la semaine manquée.

### Palettes volumétriques par région

EDI TRANSPORT compare cinq périodes : la date analysée, la veille à la même heure et en journée complète, puis J−7 à la même heure et en journée complète. Les journées vont de 4 h inclus à 4 h exclu le lendemain; pour une date passée, les comparaisons à la même heure couvrent aussi la journée complète. Les colonnes sont regroupées en bleu ardoise, vert grisé et mauve grisé. Toutes les palettes du tableau et la carte Palettes linehaul utilisent le volume par profil client. La carte reprend exactement le total des arrondis régionaux de la date analysée et se recalcule avec les réglages de hauteur et de remplissage; une couverture incomplète affiche « Incomplet ».

Chaque date utilise ses propres profils sur les 28 journées complètes précédentes (minimum 20 mesures valides par client, remplacement par la moyenne globale pondérée sinon). Les mêmes dimensions et le même remplissage de palette s’appliquent aux cinq périodes. Les clients pneus 154810, 300968 et 300430 sont exclus des volumes de colis et des palettes des cinq périodes; leur volume de la période principale est affiché dans une colonne Pneus distincte. Les totaux additionnent les arrondis par région; une couverture incomplète rend uniquement l’estimation concernée indisponible. Les profils restent en cache par date pendant 24 h. Vérifications : `node tests/EdiForecastChecks/pallet-render-checks.mjs` et `py tests/EdiForecastChecks/transport-comparison-query-checks.py`.

L’estimation utilise le mix client de chaque période (`shipment.PARCEL_NB`, mêmes régions et exclusions que les volumes existants). Les profils sont les moyennes de `parcel.LENGTH * WIDTH * HEIGHT`, par client, sur les 28 jours complets précédents, avec dimensions strictement positives. Minimum 20 mesures par client; sinon moyenne globale pondérée par le nombre de colis dimensionnés. Les nombres de colis avec profil client, avec remplacement global et sans profil sont exposés par région et période. Sans profil utilisable pour une partie des colis, l'estimation régionale reste indisponible.

Confirmé par l'utilisateur : dimensions source en pouces et palette de 40 × 48 × 84 po, base incluse dans les 84 po. Réglages estimés : hauteur utile 78 po (réserve de 6 po pour la base), remplissage 70 %. L'unité est verrouillée en pouces; les contrôles permettent une réserve de base de 4, 6 ou 8 po et un remplissage de 60 à 80 %, mémorisés dans le navigateur. Les anciennes sélections en centimètres ou sans réserve de base sont ignorées. Le calcul arrondit chaque région à l'entier supérieur avant de sommer. C'est un modèle volumétrique, sans placement géométrique ni contraintes de poids, de fragilité ou de tournées. Vérifications : `node tests/EdiForecastChecks/pallet-render-checks.mjs`.

La ligne des colis du jour inclut une « Finale estimée » actualisée toutes les 60 secondes. Elle divise le volume déjà créé par la proportion pondérée reçue à la même heure pour les mêmes jours de semaine des huit semaines précédentes (journées de 4 h à 4 h). Les poids vont de 1 à N du plus ancien au plus récent; le numérateur et le dénominateur de la proportion utilisent les mêmes poids. Minimum quatre journées valides, une heure écoulée, des colis créés et une proportion historique d'au moins 5 %. Les jours fériés et la période Cyber Monday ne sont pas assimilés aux journées ordinaires. Le détail expose les références, leurs volumes partiels et finaux et les poids. Les dates terminées affichent le total observé. Cette projection dynamique n'écrase pas les prévisions à sept jours archivées.

La page EDI prévoit la date de référence et les six jours suivants à partir du total `parcel`, hors statuts 500/501, par journée de 4 h à 4 h. Elle utilise les huit semaines précédentes, compare les mêmes jours de semaine et pondère les observations disponibles de 1 à N (la plus récente reçoit le plus de poids). Il faut au moins quatre observations; les jours sans lignes source restent inconnus. La journée de référence et les dates suivantes sont exclues du calcul.

Les jours fériés du Québec sont exclus des références et de l'évaluation rétrospective, avec exclusion prudente du Vendredi saint et du lundi de Pâques. Le calendrier est défini dans `EdiHolidayCalendar.cs`; les congés propres à l'entreprise et reports additionnels se configurent via `EDI_EXTRA_CLOSURES=2026-12-28,2027-01-04`. Un jour férié futur n'est pas assimilé à une journée ordinaire : sa prévision est indisponible. Les promotions et changements de clients ne sont pas modélisés.

Chaque prévision expose les dates, volumes, poids et extrêmes historiques utilisés. Le total est affiché uniquement si les sept jours sont calculables. Une lecture agrégée de 84 jours permet aussi quatre tests rétrospectifs sur des horizons de sept jours : erreur absolue moyenne en colis et erreur absolue cumulée rapportée au volume réel cumulé. Les statuts actuels et corrections tardives limitent cette reconstitution.

Le modèle v3 lit 455 journées une fois au renouvellement pour intégrer les références annuelles; la consultation des prévisions archivées conserve la lecture récente de 84 jours pour le réel. Pour les jours ordinaires, il combine 50 % de moyenne récente et 50 % de moyenne annuelle ajustée (même jour de semaine à J−364 et ±7 jours, minimum deux observations). Le facteur d'activité est le rapport des sommes sur les jours appariés des 56 jours précédents et leurs homologues à J−364, avec au moins 21 paires et un dénominateur positif. Fériés et période Cyber Monday sont exclus des deux côtés. Le partage 50/50 est fixé avant le test, pas optimisé sur ses résultats.

Du lundi précédant le Cyber Monday au deuxième dimanche suivant (J−7 à J+13), la référence est la même position par rapport au Cyber Monday N−1, multipliée par le facteur d'activité. Le Cyber Monday suit le quatrième jeudi de novembre; ce repérage gère aussi les décalages de 53 semaines. Aucun mélange avec les lundis ordinaires ne vient atténuer le pic. Cette période est exclue de la tendance ordinaire et du calcul du facteur d'activité. Si la référence annuelle événementielle manque, la prévision reste indisponible; ailleurs, l'absence d'historique annuel entraîne un retour à la tendance récente. Le min.–max. affiché concerne les références récentes, pas un intervalle prédictif.

La page expose les références annuelles, le facteur d'activité et ses paires sources, le dernier volume Cyber Monday disponible, ainsi que l'erreur du modèle v3 et du modèle v2 évalués sur exactement les mêmes jours des quatre semaines rétrospectives. Cette évaluation récente ne garantit pas la précision du pic commercial. Les archives v1 et v2 restent intactes; v3 utilise de nouveaux identifiants de fichiers.

Le service `EdiForecastRefreshService` se réveille chaque jour à 6 h, fuseau `America/Toronto`, pour enregistrer le réel des journées terminées. Le samedi à 6 h, il crée aussi la version figée couvrant ce samedi jusqu'au vendredi suivant. Avant 6 h le samedi, la semaine précédente reste active. Au redémarrage, la semaine courante peut être rattrapée avec l'horodatage réel, mais son calcul conserve le samedi comme date de coupure et n'utilise donc aucune donnée de la semaine en cours. En cas d'erreur, il conserve les archives existantes et réessaie dans une minute.

Les archives JSON sont immuables dans `ConveyorDashboard/App_Data/edi-forecasts` (ou `EDI_FORECAST_PATH`). Chaque version est écrite dans un fichier temporaire puis renommée atomiquement avant d'être affichée; le fichier précédent n'est jamais remplacé. Ne pas effacer ce dossier lors d'une mise à jour; l'inclure dans les sauvegardes. Compose monte ce dossier depuis l'hôte et règle le fuseau du conteneur. Il est exclu de Git et de l'image Docker.

« Prévu et réel » affiche les 30 dernières versions, filtrables, et compare uniquement les journées terminées de l'historique chargé. Les versions v6 couvrent toujours le samedi de référence à J+6, soit jusqu'au vendredi. Pendant la semaine, le réel apparaît du samedi jusqu'à la veille; la journée courante et les jours futurs restent vides. Les anciennes versions quotidiennes v4/v5 conservent leur horizon et leurs règles d'éligibilité d'origine. Le réel peut évoluer lors de corrections de la source; les prévisions ne changent pas. Pour une semaine passée sans archive, l'écran indique explicitement une prévision indisponible.

Vérifications du calcul : `dotnet run --project tests/EdiForecastChecks` depuis la racine du dépôt. Redémarrer le serveur après compilation pour activer le nouveau champ `forecast` de `/api/edi`.

Dans LIVRAISON PRÉVISION, chaque secteur affiche aussi `sector_info.SECTOR_CONTACT` et le nombre de routes distinctes rattachées au secteur ayant au moins une expédition avec `PARCEL_NB>0` sur les 28 derniers jours complets (`shipment.INSERT_DATE`, hors statuts 500/501, destination `DEST_ROUTE_ID`). Ces métadonnées actuelles sont enrichies avec le relevé quotidien, même pour une ancienne prévision, sans réécrire les archives. Le nombre de codes postaux est conservé.
Le réel utilise exactement les mêmes colis uniques triés et le même regroupement par date de livraison que la prévision. À 6 h chaque jour, les cohortes terminées (y compris celle du jour, tri clos à 3 h) sont relues pour la semaine; les corrections tardives peuvent ajuster le réel. Les relevés sont conservés dans `weekly-saturday/actuals/samedi-date_du_relevé.json`. Les dates futures et journées sources incomplètes restent indisponibles, jamais converties à zéro. Le navigateur programme une seule actualisation à la prochaine échéance quotidienne, sans interrogation chaque minute; le bouton Actualiser relit le relevé sauvegardé, sans recomputation dans la journée. Les tests de persistance et de calendrier incluent dimanche, lundi avant/après 6 h, mardi, redémarrage, corrections et semaine manquée.

EDI PRÉVISION charge exclusivement `/api/edi/forecasts` : lecture des archives et du réel sauvegardé, aucune requête MySQL et aucun recalcul depuis cette page. Le traitement de fond crée la prévision hebdomadaire le samedi à 6 h et enregistre le réel des 84 derniers jours complets dans `edi-forecasts/actuals/yyyy-MM-dd.json` une fois par jour. Le navigateur recharge à la prochaine échéance quotidienne du réel; il affiche séparément la date du prochain calcul hebdomadaire. Si le traitement de fond est encore en cours ou a échoué, il relit seulement les archives après une minute.

Le tableau EDI PRÉVISION réunit désormais les colonnes prévu, réel et écart (réel − prévu), avec les explications du même instantané. Le sélecteur de version charge une archive précise via `/api/edi/forecasts?version=...`, sans accès MySQL ni recalcul. Les écarts ne sont affichés que pour les journées évaluables de cette version; les totaux réels restent incomplets tant que les sept journées ne sont pas disponibles.

### Chargement EDI par page

`/api/edi` charge les volumes du jour, la semaine et la finale intrajournalière. `/api/edi/clients` charge uniquement les tendances clients; `/api/edi/transport` charge les comparaisons régionales et les profils de dimensions. Les réponses sont partagées en mémoire pendant 60 secondes, par date et page, avec verrouillage des recalculs concurrents et limite de 48 entrées. Les profils de dimensions des 28 jours précédents sont réutilisés pendant 24 heures par date analysée; aucun profil d’une autre date n’est utilisé. Le redémarrage vide ces caches. L’historique de 84 jours pour les archives provient du relevé quotidien sauvegardé, sans requête historique au chargement. Les bornes de la semaine sont passées directement comme paramètres SQL pour permettre une lecture bornée par date.

Vérification des résultats sur une journée terminée : `node tests/EdiForecastChecks/edi-page-isolation-checks.cjs <ancienne-api.json> <edi.json> <clients.json> <transport.json>`.

Le workflow déploie le code depuis `dashboard-source`; les données permanentes sont dans `/opt/conveyordashboard/data`, monté sur `/app/App_Data`. Ce dossier est indépendant du dépôt Git, du runner et des images Docker. Pour un autre environnement, `APP_DATA_PATH` peut remplacer le montage par défaut de Compose.

### Colis EDI par dépôt de destination

La page EDI charge `/api/edi/depots` indépendamment des compteurs. Le tableau compte les lignes `parcel` hors statuts 500/501, avec la même borne de fin que le compteur EDI en cache (`nowcast.asOf`), pour la journée opérationnelle et D−7. La destination vient du code postal normalisé dans `location.DEPOTNUMBER`, puis en repli de `route.END_DEPOT_ID` et `sector_info.DEPOTNUMBER` via l’expédition (`SHIPPING_ID` + `EXP_DATE`). Les candidats sont regroupés avant les sommes : une correspondance ambiguë ou manquante reste dans une ligne distincte, sans multiplication ni perte des colis. Réponses en cache 60 secondes, limitées à 16 entrées et identifiées par date et borne de fin. Validation : `node tests/EdiForecastChecks/depot-render-checks.cjs <depots.json> <edi.json>`.

### Conservation des prévisions pendant les déploiements

`deploy/deploy-dashboard.sh` construit l’image avant d’arrêter brièvement le service. Il sauvegarde les archives et modèles existants dans `/opt/conveyordashboard/backups/<run>-<tentative>/`, puis récupère les JSON et modèles `.zip` manquants depuis le montage actif et les anciens dossiers du runner vers le stockage permanent. Aucun ancien fichier n’est supprimé ou écrasé; une version conflictuelle est conservée dans la sauvegarde. Les sommes SHA-256 de toutes les archives et de tous les modèles actifs sont contrôlées après le remplacement du conteneur, ainsi que le montage effectif. Les archives EDI, les prévisions de livraison, les relevés réels et les modèles ML.NET sont tous inclus. Les déploiements en cours ne sont plus annulés par un nouveau commit. Aucun nettoyage automatique des archives ou sauvegardes n’est configuré. Pour résister aussi à une panne du disque ou du serveur, sauvegarder ces deux dossiers sur un support distinct.

Récupération du 13 septembre 2026 : neuf archives manquantes ont été restaurées depuis la copie locale. Trois fichiers divergents ont été conservés à part sans remplacer les versions actives. Le lot original et ces conflits sont dans `/opt/conveyordashboard/data/recovery-backups/2026-09-13/`. L’accès temporaire authentifié utilisé pour cette opération a été retiré après restauration.

Le clic sur un dépôt ouvre les clients expéditeurs de la journée, classés par volume décroissant. Les dix premiers sont affichés par défaut; « Tous les clients » utilise la même réponse, sans nouvelle requête SQL. Le détail utilise `parcel.CUSTOMER_ID`, joint au nom dans `customer`, et partage le cache et la borne horaire du tableau des dépôts.

Chaque page affiche `/api/deployment` dans son pied de page. `DEPLOYED_AT` est fixé en UTC immédiatement avant le remplacement du conteneur par le script de déploiement, puis affiché dans le fuseau Montréal. Une exécution locale sans cette variable affiche une date indisponible, sans inventer une date de déploiement à partir du démarrage.

Depuis la version `weekly-saturday-v6-lightgbm-challenger`, le renouvellement national est hebdomadaire : les fichiers `yyyy-MM-dd-v6.json` sont créés pour un samedi et couvrent ce samedi à J+6. Ils préservent toutes les anciennes archives quotidiennes. Le test rétrospectif national conserve des horizons de sept jours avec des observations strictement antérieures au samedi de calcul. Le calendrier hebdomadaire des prévisions de livraison par secteur reste inchangé.

### Carte des envois EDI

Le bouton « Carte des envois » ouvre un dialogue avec les destinations de la date sélectionnée. `/api/edi/map` réutilise la borne horaire des compteurs EDI et compte les mêmes lignes `parcel`, hors statuts 500/501. Les coordonnées `shipment.DEST_LAT/DEST_LON` sont prioritaires, avec repli sur la position unique du code postal dans `location`; les coordonnées hors limites ou (0,0) sont rejetées. Les correspondances multiples incompatibles restent non positionnées. Les points sont regroupés à six décimales, et les clusters affichent la somme des colis. Le total positionné + non positionné se réconcilie avec le compteur. Les positions par code postal sont signalées comme approximatives. Aucun nom, adresse ou identifiant de destinataire n’est exposé par cette API.

Carte et données se chargent uniquement au clic. Réponses en cache 60 secondes, maximum huit entrées par date/borne horaire. Leaflet 1.9.4 et MarkerCluster 1.5.3 sont servis localement avec licences et empreintes dans `wwwroot/vendor`. Le fond cartographique provient des tuiles OpenStreetMap chargées par le navigateur selon la zone visible; aucune requête de géocodage externe, aucun envoi de volumes ou de données de destinataires au fournisseur. Attribution visible et cache HTTP des tuiles conservé. Validation : `node tests/EdiForecastChecks/map-render-checks.cjs <carte.json> <edi.json>`.
