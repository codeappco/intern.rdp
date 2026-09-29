$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'stajprojesi uzaktan baglanti/RemoteDesk.Server/RemoteDesk.Server.csproj'
dotnet build $project -o (Join-Path $root '.test-build/server') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Server build failed' }
$serverPath = Join-Path $root '.test-build/server/RemoteDesk.Server.dll'
$server = Start-Process dotnet -ArgumentList @(('"' + $serverPath + '"'), '--urls', 'http://127.0.0.1:5099', '--environment', 'Production') -WindowStyle Hidden -PassThru
$clients = @()
function Receive($ws) {
    $buffer = [byte[]]::new(65536)
    $stream = [IO.MemoryStream]::new()
    $timeout = [Threading.CancellationTokenSource]::new(5000)
    try {
        do {
            $result = $ws.ReceiveAsync([ArraySegment[byte]]::new($buffer), $timeout.Token).GetAwaiter().GetResult()
            $stream.Write($buffer, 0, $result.Count)
        } while (-not $result.EndOfMessage)
        return ([Text.Encoding]::UTF8.GetString($stream.ToArray()) | ConvertFrom-Json)
    } finally { $timeout.Dispose(); $stream.Dispose() }
}
function Send($ws, $value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes(($value | ConvertTo-Json -Compress))
    for ($offset = 0; $offset -lt $bytes.Length; $offset += 2048) {
        $count = [Math]::Min(2048, $bytes.Length - $offset)
        $null = $ws.SendAsync([ArraySegment[byte]]::new($bytes, $offset, $count), [Net.WebSockets.WebSocketMessageType]::Text, ($offset + $count -eq $bytes.Length), [Threading.CancellationToken]::None).GetAwaiter().GetResult()
    }
}
function Check($condition, $label) { if (-not $condition) { throw "FAIL: $label" }; Write-Output "PASS: $label" }
function Request($hostClient, $viewer, $code) {
    Start-Sleep -Milliseconds 1100
    Send $viewer @{tip='baglan'; kod=$code}
    $request = Receive $hostClient
    $null = Receive $viewer
    return $request.istekId
}
try {
    Start-Sleep -Seconds 2
    Check ((Invoke-RestMethod 'http://127.0.0.1:5099/health').durum -eq 'hazir') 'Public health endpoint is ready'
    foreach ($path in @('/baglanti-kodu/12345678', '/baglanti-kodu')) {
        $status = 0
        try { $null = Invoke-WebRequest ('http://127.0.0.1:5099' + $path) -Method $(if ($path -eq '/baglanti-kodu') { 'POST' } else { 'GET' }) }
        catch { $status = [int]$_.Exception.Response.StatusCode }
        Check ($status -eq 404) "Development endpoint disabled: $path"
    }
    1..3 | ForEach-Object {
        $ws = [Net.WebSockets.ClientWebSocket]::new()
        $null = $ws.ConnectAsync([Uri]'ws://127.0.0.1:5099/ws', [Threading.CancellationToken]::None).GetAwaiter().GetResult()
        $clients += $ws
        $null = Receive $ws
    }
    $hostClient, $viewer, $stranger = $clients
    Send $hostClient @{tip='kod_iste'}
    $code = (Receive $hostClient).kod
    Send $hostClient @{tip='kod_iste'}
    Check ((Receive $hostClient).tip -eq 'bilgi') 'Rapid code generation is limited'
    Start-Sleep -Milliseconds 2100
    Send $hostClient @{tip='kod_iste'}
    $newCode = (Receive $hostClient).kod
    Send $stranger @{tip='baglan'; kod=$code}
    Check ((Receive $stranger).mesaj -like 'Kod gecersiz*') 'Generating a new code invalidates the old code'
    $code = $newCode
    $id = Request $hostClient $viewer $code
    Send $hostClient @{tip='baglanti_cevabi'; istekId=$id; kabul=$false}
    Check ((Receive $viewer).durum -eq 'ret') 'Rejection does not start a session'
    $null = Receive $hostClient
    $id = Request $hostClient $viewer $code
    Send $stranger @{tip='baglanti_cevabi'; istekId=$id; kabul=$true}
    Check ((Receive $stranger).tip -eq 'bilgi') 'Stranger cannot accept'
    Send $hostClient @{tip='baglanti_cevabi'; istekId=$id; kabul=$true}
    Check ((Receive $viewer).rol -eq 'izleyen') 'Viewer starts after acceptance'
    Check ((Receive $hostClient).rol -eq 'paylasan') 'Host starts after acceptance'
    Send $viewer @{tip='girdi'; oturumId=$id; izin='missing'; eylem='birak'}
    $permission = [Guid]::NewGuid().ToString()
    Send $hostClient @{tip='kontrol_izni'; oturumId=$id; izin=$permission}
    Check ((Receive $viewer).izin -eq $permission) 'Control permission reaches viewer'
    Send $stranger @{tip='girdi'; oturumId=$id; izin=$permission; eylem='unauthorized'}
    Send $viewer @{tip='girdi'; oturumId=$id; izin=$permission; eylem='birak'}
    $inputMessage = Receive $hostClient
    Check ($inputMessage.tip -eq 'girdi' -and $inputMessage.izin -eq $permission -and $inputMessage.eylem -eq 'birak') 'Only authorized viewer input reaches host'
    Send $hostClient @{tip='kontrol_izni'; oturumId=$id; izin=''}
    Check ((Receive $viewer).izin -eq '') 'Control revocation reaches viewer'
    Send $viewer @{tip='girdi'; oturumId=$id; izin=$permission; eylem='birak'}
    Send $stranger @{tip='ekran'; oturumId=$id; jpeg='unauthorized'}
    Send $stranger @{tip='oturum_bitir'; oturumId=$id}
    Send $hostClient @{tip='ekran'; oturumId='invalid'; jpeg='invalid-session'}
    $frame = 'a' * 40000
    Send $hostClient @{tip='ekran'; oturumId=$id; jpeg=$frame}
    Check ((Receive $viewer).jpeg -eq $frame) 'Fragmented frame forwarded; stranger and invalid session ignored'
    Send $stranger @{tip='baglan'; kod=$code}
    Check ((Receive $stranger).tip -eq 'bilgi') 'Active host rejects another request'
    Send $viewer @{tip='oturum_bitir'; oturumId=$id}
    Check ((Receive $hostClient).tip -eq 'oturum_bitti') 'Viewer stop reaches host'
    Check ((Receive $viewer).tip -eq 'oturum_bitti') 'Viewer stop acknowledged'
    Send $hostClient @{tip='ekran'; oturumId=$id; jpeg='stale'}
    Send $viewer @{tip='kod_iste'}
    Check ((Receive $viewer).tip -eq 'kod_olusturuldu') 'Stopped session cannot forward frames'
    $id = Request $hostClient $viewer $code
    Send $hostClient @{tip='baglanti_cevabi'; istekId=$id; kabul=$true}
    $null = Receive $viewer
    $null = Receive $hostClient
    $viewer.Abort()
    Check ((Receive $hostClient).tip -eq 'oturum_bitti') 'Disconnect stops sharing'
} finally {
    foreach ($ws in $clients) { $ws.Abort(); $ws.Dispose() }
    if (-not $server.HasExited) { Stop-Process -Id $server.Id }
}

