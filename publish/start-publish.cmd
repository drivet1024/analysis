@echo off
setlocal
cd /d "%~dp0"
where python >nul 2>&1
if errorlevel 1 (
  echo Python est requis pour demarrer le site.
  pause
  exit /b 1
)
echo Serveur demarre sur le port 8765.
echo Depuis ce poste : http://127.0.0.1:8765/
echo Depuis le reseau : http://ADRESSE-IP-DE-CE-POSTE:8765/
start "Centre d'analyse" http://127.0.0.1:8765/
python -m http.server 8765 --bind 0.0.0.0
