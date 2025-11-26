# Check WebSocket connection stats
Write-Host "=== MEXC WebSocket Connection Statistics ===" -ForegroundColor Cyan

# Read the log file if it exists
$logPath = "C:\visual projects\arb1\collections\logs\websocket.log"

if (Test-Path $logPath) {
    Write-Host "`nWebSocket Log (last 50 lines):" -ForegroundColor Yellow
    Get-Content $logPath -Tail 50
} else {
    Write-Host "`nLog file not found at: $logPath" -ForegroundColor Red
}

# Also check console output via netstat
Write-Host "`n=== Active WebSocket Connections (netstat) ===" -ForegroundColor Cyan
$connections = netstat -ano | Select-String "ESTABLISHED" | Select-String "mexc"
if ($connections) {
    Write-Host "MEXC connections found:"
    $connections
} else {
    Write-Host "No active MEXC connections found in netstat" -ForegroundColor Yellow
}
