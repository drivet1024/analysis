$ErrorActionPreference = 'Stop'

$secureKey = Read-Host 'Collez votre clé API OpenAI (elle ne sera pas affichée)' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)

try {
    $plainKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    if ([string]::IsNullOrWhiteSpace($plainKey)) {
        throw 'La clé ne peut pas être vide.'
    }

    $model = Read-Host 'Modèle OpenAI [gpt-5.6-luna]'
    if ([string]::IsNullOrWhiteSpace($model)) {
        $model = 'gpt-5.6-luna'
    }

    $content = @(
        "OPENAI_API_KEY=$plainKey"
        "OPENAI_MODEL=$model"
    )
    [IO.File]::WriteAllLines((Join-Path $PSScriptRoot '.env.local'), $content)
    Write-Host 'Configuration OpenAI enregistrée localement.' -ForegroundColor Green
}
finally {
    if ($pointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
    $plainKey = $null
}
