$key = "CQBPYzqNTUBsnggPTh10vQMFrJeMav"
$secret = "gdsihgtEbFC8iOk7SfyiFrKSbZgmdjf6WeLR8nOxeC8CXSBbartKkGqQ8esi"
$base = "https://cdn-ind.testnet.deltaex.org"
$endTime = (Get-Date).AddMinutes(30)
$trades = @()
$runNum = 0
$totalPnl = [decimal]0

function Invoke-DeltaApi($Method, $Path, $Body) {
    $ts = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $hmac = New-Object System.Security.Cryptography.HMACSHA256
    $hmac.Key = [Text.Encoding]::UTF8.GetBytes($secret)
    $data = "$Method$ts$Path$Body"
    $sig = [BitConverter]::ToString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($data))).Replace("-","").ToLower()
    $h = @{"api-key"=$key; "timestamp"=$ts; "signature"=$sig; "User-Agent"="ETHWyckoffBot/1.0"; "Content-Type"="application/json"}
    if ($Body) { $r = Invoke-WebRequest -Uri "$base$Path" -Method $Method -Headers $h -Body $Body -TimeoutSec 10 }
    else { $r = Invoke-WebRequest -Uri "$base$Path" -Method $Method -Headers $h -TimeoutSec 10 }
    return ($r.Content | ConvertFrom-Json)
}

function Get-Price {
    $ua = @{"User-Agent"="ETHWyckoffBot/1.0"}
    $r = Invoke-WebRequest -Uri "$base/v2/tickers?symbols=ETHUSD" -Headers $ua -TimeoutSec 10
    $json = $r.Content | ConvertFrom-Json
    $eth = $json.result | Where-Object { $_.symbol -eq "ETHUSD" } | Select-Object -First 1
    return [decimal]$eth.close
}

function Get-Balance {
    $r = Invoke-DeltaApi "GET" "/v2/wallet/balances"
    $usd = $r.result | Where-Object { $_.asset_symbol -eq "USD" } | Select-Object -First 1
    return [decimal]$usd.balance
}

function Get-Candles($Resolution, $Count) {
    $ua = @{"User-Agent"="ETHWyckoffBot/1.0"}
    $end = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $resSec = @{"1m"=60;"5m"=300;"15m"=900}[$Resolution]
    $start = $end - ($resSec * ($Count + 2))
    $r = Invoke-WebRequest -Uri "$base/v2/history/candles?symbol=ETHUSD&resolution=$Resolution&start=$start&end=$end" -Headers $ua -TimeoutSec 10
    return ($r.Content | ConvertFrom-Json).result
}

function Calc-ATR($Candles, $Period) {
    if ($Candles.Count -lt 2) { return 0 }
    $trs = for ($i = 1; $i -lt $Candles.Count; $i++) {
        $h = [decimal]$Candles[$i].high; $l = [decimal]$Candles[$i].low; $pc = [decimal]$Candles[$i-1].close
        [Math]::Max($h - $l, [Math]::Max([Math]::Abs($h - $pc), [Math]::Abs($l - $pc)))
    }
    $atr = ($trs | Select-Object -Last $Period | Measure-Object -Average).Average
    return $atr
}

function Calc-Supertrend($Candles, $Period, $Multiplier) {
    if ($Candles.Count -lt $Period + 1) { return @{Trend="NONE";Band=0;Atr=0} }
    $closes = $Candles | ForEach-Object { [decimal]$_.close }
    $atr = Calc-ATR $Candles $Period
    if ($atr -eq 0) { return @{Trend="NONE";Band=0;Atr=0} }
    $hl2 = ($Candles[-1] | ForEach-Object { ([decimal]$_.high + [decimal]$_.low) / 2 })
    $upperBand = $hl2 + $Multiplier * $atr
    $lowerBand = $hl2 - $Multiplier * $atr
    
    if ($Candles.Count -lt 2) { return @{Trend="NONE";Band=$lowerBand;Atr=$atr} }
    $prevClose = [decimal]$Candles[-2].close
    $trend = if ($prevClose -gt $lowerBand) { "UP" } else { "DOWN" }
    return @{Trend=$trend;Band=$lowerBand;Atr=$atr}
}

function Place-Order($Side, $Contracts) {
    $b = '{"product_id":1699,"size":' + $Contracts + ',"side":"' + $Side + '","order_type":"market_order"}'
    $r = Invoke-DeltaApi "POST" "/v2/orders" $b
    Write-Host "  Order: success=$($r.success) fill=$($r.result.average_fill_price)" -ForegroundColor Gray
    return ($r.success -eq $true)
}

function Close-Position {
    $r = Invoke-DeltaApi "GET" "/v2/positions?product_id=1699"
    $size = [long]$r.result.size
    if ($size -eq 0) { return $true, [decimal]0 }
    $side = if ($size -gt 0) { "sell" } else { "buy" }
    $abs = [Math]::Abs($size)
    $b = '{"product_id":1699,"size":' + $abs + ',"side":"' + $side + '","order_type":"market_order","reduce_only":true}'
    $r2 = Invoke-DeltaApi "POST" "/v2/orders" $b
    $pnl = [decimal]$r2.result.meta_data.pnl
    return $true, $pnl
}

# === INIT ===
Write-Host "=== ETH BOT (SUPERTREND, 5 ETH EXPOSURE) ===" -ForegroundColor Cyan
$price = Get-Price
$bal = Get-Balance
Write-Host "Connected: ETHUSD=$('{0:F2}' -f $price) Balance=$('{0:F2}' -f $bal) USD" -ForegroundColor Green

$r = Invoke-DeltaApi "GET" "/v2/positions?product_id=1699"
$size = [long]$r.result.size
$inPosition = $size -ne 0
$entryPrice = [decimal]0; $entryDir = ""; $entryQty = [decimal]0

# Load initial candles
$c5m = Get-Candles "5m" 30
$c15m = Get-Candles "15m" 30
Write-Host "Candles: 5m=$($c5m.Count) 15m=$($c15m.Count)" -ForegroundColor Gray

# === LOOP ===
while ((Get-Date) -lt $endTime) {
    $runNum++
    Write-Host "`n--- Run $runNum $(Get-Date -Format HH:mm:ss) ---" -ForegroundColor Magenta
    try {
        $price = Get-Price
        $bal = Get-Balance

        $c5m = Get-Candles "5m" 30
        $c15m = Get-Candles "15m" 30

        # Supertrend
        $st5 = Calc-Supertrend $c5m 10 3
        $st15 = Calc-Supertrend $c15m 10 3
        $stock5 = [decimal]$c5m[-1].close
        $atr5 = $st5.Atr

        Write-Host "Price: $('{0:F2}' -f $price) | Bal: $('{0:F2}' -f $bal) USD"
        Write-Host "Supertrend 5m: $($st5.Trend) (ATR=$('{0:F2}' -f $atr5) Band=$('{0:F2}' -f $st5.Band))"
        Write-Host "Supertrend 15m: $($st15.Trend)"

        # Check position
        $r = Invoke-DeltaApi "GET" "/v2/positions?product_id=1699"
        $size = [long]$r.result.size
        if ($size -ne 0) {
            $inPosition = $true
            $entryPrice = [decimal]$r.result.entry_price
            $entryDir = if ($size -gt 0) { "LONG" } else { "SHORT" }
            $entryQty = [Math]::Abs($size) * [decimal]0.01
            $upnl = ($price - $entryPrice) * $entryQty * $(if($entryDir -eq "LONG"){[decimal]1}else{[decimal]-1})
            $upct = if($entryPrice -gt 0){($upnl/($entryPrice*$entryQty))*100}else{[decimal]0}
            Write-Host "Position: $entryDir $('{0:F4}' -f $entryQty) ETH @ $('{0:F2}' -f $entryPrice) | UPnL: $('{0:F4}' -f $upnl) ($('{0:F2}' -f $upct)%)" -ForegroundColor Yellow
        } else {
            if ($inPosition) { Write-Host "Position closed" -ForegroundColor Red }
            $inPosition = $false
        }

        # === SUPERTREND STRATEGY ===
        # Entry: 500 contracts = 5 ETH exposure
        $targetContracts = 500
        $maxByBal = [int]($bal * 30 / $price / [decimal]0.01)  # allow up to 30x leverage
        $contracts = [Math]::Min($targetContracts, $maxByBal)
        $contracts = [Math]::Max(1, $contracts)

        if (-not $inPosition) {
            if ($st5.Trend -eq "UP" -and $st15.Trend -eq "UP") {
                Write-Host "SUPERTREND BUY SIGNAL: $contracts contracts ($($contracts*0.01) ETH @ ~$('{0:F2}' -f ($contracts*0.01*$price)) USD notiona)" -ForegroundColor Green
                $ok = Place-Order "buy" $contracts
                if ($ok) {
                    Write-Host ">> ENTERED LONG <<" -ForegroundColor Green
                    $trades += @{Time=Get-Date;Type="ENTRY";Dir="LONG";Contracts=$contracts}
                    $inPosition = $true
                }
            } elseif ($st5.Trend -eq "DOWN" -and $st15.Trend -eq "DOWN") {
                Write-Host "SUPERTREND SELL SIGNAL: $contracts contracts ($($contracts*0.01) ETH)" -ForegroundColor Green
                $ok = Place-Order "sell" $contracts
                if ($ok) {
                    Write-Host ">> ENTERED SHORT <<" -ForegroundColor Green
                    $trades += @{Time=Get-Date;Type="ENTRY";Dir="SHORT";Contracts=$contracts}
                    $inPosition = $true
                }
            }
        }

        # Exit: opposite supertrend signal
        if ($inPosition) {
            $exitReason = ""
            if ($entryDir -eq "LONG" -and $st5.Trend -eq "DOWN") { $exitReason = "SUPERTREND REVERSAL (5m)" }
            if ($entryDir -eq "SHORT" -and $st5.Trend -eq "UP") { $exitReason = "SUPERTREND REVERSAL (5m)" }

            # Trailing stop based on ATR
            if ($entryDir -eq "LONG" -and $atr5 -gt 0) {
                $sl = $stock5 - ($atr5 * 2)
                if ($price -le $sl) { $exitReason = "TRAILING STOP (ATRx2)" }
            }
            if ($entryDir -eq "SHORT" -and $atr5 -gt 0) {
                $sl = $stock5 + ($atr5 * 2)
                if ($price -ge $sl) { $exitReason = "TRAILING STOP (ATRx2)" }
            }

            if ($exitReason) {
                Write-Host "EXIT SIGNAL: $exitReason" -ForegroundColor Red
                $ok, $pnl = Close-Position
                if ($ok) {
                    $totalPnl += $pnl
                    $pct = if($entryPrice -gt 0){($pnl/($entryPrice*$entryQty))*100}else{[decimal]0}
                    Write-Host ">> EXITED << PnL: $('{0:F4}' -f $pnl) USD ($('{0:F2}' -f $pct)%)" -ForegroundColor $(if($pnl -ge 0){'Green'}else{'Red'})
                    $trades += @{Time=Get-Date;Type="EXIT";Dir=$entryDir;Pnl=$pnl;Pct=$pct;Reason=$exitReason}
                    $inPosition = $false
                }
            }
        }
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
        Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
    }
    Start-Sleep -Seconds 60
}

# === FINAL REPORT ===
try {
    $r = Invoke-DeltaApi "GET" "/v2/positions?product_id=1699"
    if ([long]$r.result.size -ne 0) { $ok, $pnl = Close-Position; $totalPnl += $pnl; $trades += @{Time=Get-Date;Type="FINAL_EXIT";Pnl=$pnl} }
} catch {}
$finalBal = Get-Balance
$price = Get-Price

$initialBal = [decimal]131.87
Write-Host "`n`n==========================================" -ForegroundColor Cyan
Write-Host "           PERFORMANCE REPORT" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Duration: $runNum minutes" -ForegroundColor White
Write-Host "Symbol: ETHUSD @ $('{0:F2}' -f $price)" -ForegroundColor White

$entries = $trades | Where-Object { $_.Type -eq 'ENTRY' }
$exits = $trades | Where-Object { $_.Type -eq 'EXIT' -or $_.Type -eq 'FINAL_EXIT' }
Write-Host "Entries: $($entries.Count) | Exits: $($exits.Count)" -ForegroundColor White

Write-Host "`nBalance: $('{0:F2}' -f $initialBal) -> $('{0:F2}' -f $finalBal) USD" -ForegroundColor White
$change = $finalBal - $initialBal
$changePct = ($change / $initialBal) * 100
Write-Host "Change:  $('{0:F2}' -f $change) USD ($('{0:F2}' -f $changePct)%)" -ForegroundColor $(if($change -ge 0){'Green'}else{'Red'})
Write-Host "Bot PnL: $('{0:F4}' -f $totalPnl) USD" -ForegroundColor $(if($totalPnl -ge 0){'Green'}else{'Red'})

Write-Host "`nTrade Log:" -ForegroundColor Cyan
$trades | ForEach-Object {
    if ($_.Type -eq "ENTRY") {
        Write-Host "  $($_.Time.ToString('HH:mm:ss')) ENTRY $($_.Dir) $($_.Contracts) contracts" -ForegroundColor Green
    } else {
        $c = if ($_.Pnl -ge 0) { 'Green' } else { 'Red' }
        Write-Host "  $($_.Time.ToString('HH:mm:ss')) EXIT  $($_.Dir) PnL=$('{0:F4}' -f $_.Pnl) USD ($('{0:F2}' -f $_.Pct)%) $($_.Reason)" -ForegroundColor $c
    }
}
Write-Host "`nBot finished at $(Get-Date -Format HH:mm:ss)" -ForegroundColor Cyan
