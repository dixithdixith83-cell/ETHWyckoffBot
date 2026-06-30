# ETHWyckoffBot - VPS Deployment Script (Multi-Coin)
# Run this on the Windows VPS to deploy the bot as a background service

$BotPath = "C:\ETHWyckoffBot"
$ExeSource = ".\src\ETHWyckoffBot\bin\Release\net10.0"

Write-Host "=== ETHWyckoffBot Multi-Coin VPS Deploy ===" -ForegroundColor Cyan

Write-Host "[1/4] Building Release..." -ForegroundColor Yellow
dotnet build .\src\ETHWyckoffBot -c Release
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed!" -ForegroundColor Red; exit 1 }

Write-Host "[2/4] Copying files to $BotPath..." -ForegroundColor Yellow
if (Test-Path $BotPath) { Remove-Item "$BotPath\*" -Recurse -Force }
New-Item -ItemType Directory -Path $BotPath -Force | Out-Null
Copy-Item "$ExeSource\*" -Destination $BotPath -Recurse -Force

Write-Host "[3/4] Creating Windows Service (auto-start on boot)..." -ForegroundColor Yellow
$SvcName = "ETHWyckoffBot"
$svc = Get-Service -Name $SvcName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -eq 'Running') { Stop-Service -Name $SvcName -Force }
    sc.exe delete $SvcName
    Start-Sleep -Seconds 2
}
sc.exe create $SvcName binPath="$BotPath\ETHWyckoffBot.exe" start=auto DisplayName="ETH Wyckoff Bot"
Write-Host "  Service '$SvcName' created" -ForegroundColor Green

Write-Host "[4/4] Starting bot..." -ForegroundColor Yellow
Start-Service -Name $SvcName
Write-Host "  Bot started! Logs at $BotPath\logs\heartbeat.txt" -ForegroundColor Green

Write-Host ""
Write-Host "=== DEPLOY COMPLETE ===" -ForegroundColor Cyan
Write-Host "Trading: BTC, ETH, SOL, XRP, DOGE, ADA"
Write-Host "Max concurrent positions: 2"
Write-Host "To stop:   Stop-Service -Name '$SvcName'"
Write-Host "To remove: sc.exe delete '$SvcName'"
