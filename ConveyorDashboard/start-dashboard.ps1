param(
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'

$dashboardUrl = 'http://127.0.0.1:5077'

try {
    $status = Invoke-WebRequest -UseBasicParsing -Uri "$dashboardUrl/api/status" -TimeoutSec 3
    if ($status.StatusCode -eq 200) {
        if (-not $NoBrowser) {
            Write-Host 'Le dashboard est déjà démarré. Ouverture dans le navigateur…' -ForegroundColor Green
            Start-Process $dashboardUrl
        }
        else {
            Write-Host 'Le dashboard est déjà démarré.' -ForegroundColor Green
        }
        exit 0
    }
}
catch {
    # Aucun dashboard accessible : poursuivre avec le démarrage normal.
}

$envFile = Join-Path $PSScriptRoot '.env.local'
if (-not (Test-Path -LiteralPath $envFile) -and [string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY)) {
    Write-Host 'La clé OpenAI doit être configurée une seule fois.' -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot 'configure-openai-key.ps1')
}

# Certains environnements de développement injectent un proxy local volontairement fermé.
# Il ne doit pas être transmis au dashboard, qui appelle directement l'API OpenAI.
foreach ($proxyVariable in @('HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY')) {
    $proxyValue = [Environment]::GetEnvironmentVariable($proxyVariable)
    if ($proxyValue -match '^http://127\.0\.0\.1:9/?$') {
        [Environment]::SetEnvironmentVariable($proxyVariable, $null)
    }
}

dotnet run --project (Join-Path $PSScriptRoot 'ConveyorDashboard.csproj') --urls $dashboardUrl
