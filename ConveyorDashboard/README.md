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
