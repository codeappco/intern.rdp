$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$serverProject = Join-Path $root 'stajprojesi uzaktan baglanti/RemoteDesk.Server/RemoteDesk.Server.csproj'
$securityProject = Join-Path $PSScriptRoot 'RelaySecurity.Tests/RelaySecurity.Tests.csproj'
$e2eeProject = Join-Path $PSScriptRoot 'E2ee.Tests/E2ee.Tests.csproj'
$wpfProject = Join-Path $PSScriptRoot 'WpfSecurity.Tests/WpfSecurity.Tests.csproj'
$build = Join-Path $root '.test-build/security-server'
dotnet build $serverProject -o $build
if ($LASTEXITCODE -ne 0) { throw 'Server build failed' }
foreach ($project in @($securityProject, $e2eeProject, $wpfProject)) {
    dotnet build $project
    if ($LASTEXITCODE -ne 0) { throw "Test build failed: $project" }
}
dotnet run --no-build --project $wpfProject
if ($LASTEXITCODE -ne 0) { throw 'WPF verification tests failed' }
foreach ($suite in @('protocol', 'generation', 'v3', 'v4', 'v5', 'regression')) {
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$serverPath = Join-Path $build 'RemoteDesk.Server.dll'
$server = Start-Process dotnet -ArgumentList @(('"' + $serverPath + '"'), '--urls', "http://127.0.0.1:$port", '--ConnectionCodeLifetimeSeconds', '5', '--WebSocket:AllowedOrigins:0', 'https://trusted.example') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $build "$suite-stdout.log") -RedirectStandardError (Join-Path $build "$suite-stderr.log")
try {
    $ready = $false
    $probeError = ''
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ($server.HasExited) { throw "Test server did not start; see .test-build/security-server/$suite-stderr.log" }
        $probe = [System.Net.Sockets.TcpClient]::new()
        try { $probe.Connect('127.0.0.1', $port); $ready = $true; break }
        catch { $probeError = $_.Exception.Message; Start-Sleep -Milliseconds 100 }
        finally { $probe.Dispose() }
    }
    if (-not $ready) { throw "Test server startup timed out: $probeError" }
    if ($suite -ne 'regression') {
        dotnet run --no-build --project $securityProject -- "--$suite" "ws://127.0.0.1:$port/ws" | Tee-Object -FilePath (Join-Path $build "$suite-results.log")
        if ($LASTEXITCODE -ne 0) { throw 'Lifecycle and message-boundary tests failed' }
    } else {
    dotnet run --no-build --project $e2eeProject -- "ws://127.0.0.1:$port/ws"
    if ($LASTEXITCODE -ne 0) { throw 'E2EE regression tests failed' }
    dotnet run --no-build --project $securityProject -- "ws://127.0.0.1:$port/ws" 5
    if ($LASTEXITCODE -ne 0) { throw 'Relay security tests failed' }
    }
} finally {
    if (-not $server.HasExited) { Stop-Process -Id $server.Id }
    $server.WaitForExit()
    $server.Dispose()
}
}
$log = Get-Content (Join-Path $build 'regression-stdout.log') -Raw
if ($log -notmatch 'ConnectionCodeRejected PeerTag=[A-F0-9]{16}' -or $log -notmatch 'ConnectionAttemptLimited PeerTag=[A-F0-9]{16}') { throw 'Security audit events missing' }
if ($log -match 'LOG_INJECTION_SENTINEL|forged-log|00000000|opaque-regression-payload') { throw 'Sensitive request content leaked into logs' }
Write-Output 'PASS security audit events present without submitted codes or payloads'
$generationLog = Get-Content (Join-Path $build 'generation-stdout.log') -Raw
if (($generationLog | Select-String -Pattern 'ConnectionCodeGenerationLimited PeerTag=[A-F0-9]{16}' -AllMatches).Matches.Count -ne 2) { throw 'Generation throttling audit must log one client denial and one IP denial, suppressing repeats' }
Write-Output 'PASS generation rate-limit audit events are present and repeated denials are suppressed'
