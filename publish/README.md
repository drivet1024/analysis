# Publication du centre d'analyse

## Contenu

Ce répertoire est une publication statique. Il contient le menu, les cartes, le graphique et les exports de données pour la période du 27 juillet au 25 août 2026.

## Installation sur un serveur web

1. Copier tout le contenu de ce répertoire dans le dossier public du serveur web.
2. Configurer le document racine sur `index.html`.
3. Autoriser les fichiers `.html`, `.json` et `.csv`.
4. Ouvrir l'URL du serveur.

Les pages cartographiques chargent Leaflet et les tuiles OpenStreetMap depuis Internet. Le serveur doit donc autoriser les ressources externes du navigateur.

Ce paquet ne contient aucun accès à MySQL ni secret. Les données sont des exports statiques.

## Démarrage sans IIS sous Windows

Double-cliquer sur `start-publish.cmd`, ou exécuter :

```powershell
python -m http.server 8765 --bind 127.0.0.1
```

Puis ouvrir `http://127.0.0.1:8765/`.

## Accès réseau

Les lanceurs `start-publish.cmd` et `start-publish.ps1` écoutent sur `0.0.0.0:8765`. Depuis un autre poste, ouvrir :

`http://ADRESSE-IP-DU-POSTE-SERVEUR:8765/`

Pour connaître l'adresse IP du poste serveur :

```powershell
ipconfig
```

Si le pare-feu Windows bloque l'accès, créer une règle entrante, idéalement limitée au réseau privé :

```powershell
New-NetFirewallRule -DisplayName "Centre analyse pickups 8765" -Direction Inbound -Protocol TCP -LocalPort 8765 -Action Allow -Profile Private
```

Ce mode expose les exports statiques aux appareils autorisés sur le réseau. Ne pas exposer ce serveur directement sur Internet.
