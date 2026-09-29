param(
    [Parameter(Mandatory = $true)]
    [string]$ServerUrl
)
$ErrorActionPreference = 'Stop'
$adres = $null
if (-not [Uri]::TryCreate($ServerUrl, [UriKind]::Absolute, [ref]$adres) -or
    $adres.Scheme -ne 'wss' -or $adres.AbsolutePath -ne '/ws' -or
    $adres.UserInfo -or $adres.Fragment -or $adres.Query) {
    throw 'Sunucu adresi wss://remote.example.com/ws biciminde olmali.'
}
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti.csproj'
$output = Join-Path $root 'artifacts/client-win-x64'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $output
if ($LASTEXITCODE -ne 0) { throw 'Istemci derlenemedi.' }
@{ SunucuAdresi = $adres.AbsoluteUri } | ConvertTo-Json | Set-Content (Join-Path $output 'sunucu.json') -Encoding utf8
Write-Output "Hazir: $output (klasorun tamamini iki bilgisayara da kopyalayin)."
