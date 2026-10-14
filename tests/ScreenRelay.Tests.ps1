$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$serverProject = Join-Path $root 'stajprojesi uzaktan baglanti/RemoteDesk.Server/RemoteDesk.Server.csproj'
$testProject = Join-Path $PSScriptRoot 'E2ee.Tests/E2ee.Tests.csproj'
$build = Join-Path $root '.test-build/e2ee-server'
dotnet build $serverProject -o $build
if ($LASTEXITCODE -ne 0) { throw 'Server build failed' }
dotnet build $testProject
if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
$serverPath = Join-Path $build 'RemoteDesk.Server.dll'
$server = Start-Process dotnet -ArgumentList @(('"' + $serverPath + '"'), '--urls', 'http://127.0.0.1:15094') -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Seconds 2
    if ($server.HasExited) { throw 'Test server did not start' }
    dotnet run --no-build --project $testProject -- ws://127.0.0.1:15094/ws
    if ($LASTEXITCODE -ne 0) { throw 'E2EE relay tests failed' }
} finally { if (-not $server.HasExited) { Stop-Process -Id $server.Id } }
