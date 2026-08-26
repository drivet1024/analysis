$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
    throw 'Python est requis pour demarrer le site.'
}
Write-Host 'Serveur demarre sur le port 8765.'
Write-Host 'Depuis le reseau : http://ADRESSE-IP-DE-CE-POSTE:8765/'
Start-Process 'http://127.0.0.1:8765/'
python -m http.server 8765 --bind 0.0.0.0
