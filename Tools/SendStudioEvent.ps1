param(
    [string]$Event = 'wave',
    [double]$Value = 1,
    [int]$Port = 8010
)
$ErrorActionPreference = 'Stop'
if ($Event.Length -gt 128 -or [string]::IsNullOrWhiteSpace($Event)) { throw 'Evento inválido.' }
if ($Port -lt 1025 -or $Port -gt 65535) { throw 'Porta inválida.' }
$socket = [Net.WebSockets.ClientWebSocket]::new()
$timeout = [Threading.CancellationTokenSource]::new(5000)
try {
    $socket.ConnectAsync([Uri]('ws://127.0.0.1:' + $Port + '/avatarlab'), $timeout.Token).GetAwaiter().GetResult()
    $message = @{ event = $Event; value = $Value } | ConvertTo-Json -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($message)
    $segment = [ArraySegment[byte]]::new($bytes)
    $socket.SendAsync($segment, [Net.WebSockets.WebSocketMessageType]::Text, $true, $timeout.Token).GetAwaiter().GetResult()
    $socket.CloseOutputAsync([Net.WebSockets.WebSocketCloseStatus]::NormalClosure, '', $timeout.Token).GetAwaiter().GetResult()
    Write-Output ('Evento enviado: ' + $Event)
} finally { $socket.Dispose(); $timeout.Dispose() }
