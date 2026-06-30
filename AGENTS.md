# ETHWyckoffBot - Session Context

## Connection
- Delta Exchange testnet: `cdn-ind.testnet.deltaex.org`
- API Key: `CQBPYzqNTUBsnggPTh10vQMFrJeMav`
- Symbol: ETHUSD, Leverage: 20x
- IP whitelisted: `157.45.240.147`

## Current Strategy: Swing Trading with VWAP Mean Reversion

### Entry (ALL conditions must be met)
1. **Fusion confidence >= 0.20** (AMD + OF + Vol + VP weighted fusion)
2. **1h Supertrend direction** must match the trade direction
3. **Minimum 6 ticks wait** (6 min) between entries
4. **AMD removed as hard gate** (still contributes 15% via fusion weight)

### Exit
- **TP**: 1.80% of entry price (fixed %, managed via trailing stop)
- **SL**: 0.60% of entry price (fixed %, placed on exchange)
- **1h Supertrend reversal** → exit (primary swing exit)
- **Fusion reversed** with confidence >= 0.25 → exit
- **Max hold**: 288 ticks (~72 min)

### Key Parameters
- SL: 0.60% of price (fixed %, not ATR-based)
- TP: 1.80% of price (3:1 RR)
- Risk per trade: 5% of balance (cap $10)
- Margin per position: 60% max
- Interval: 60s between checks (was 15s)
- Daily loss limit: 50%
- Drawdown limit: 50%
- Consecutive losses halt: 4
- Risk auto-resumes on new UTC day

### PnL Tracking (FIXED)
- **REAL PnL** tracked via wallet balance difference (pre-entry vs post-exit)
- No longer using fake candle-close PnL
- Exit shows: `REAL PnL: $X.XX (bal: before -> after)`

## Issues Fixed
1. **Fake PnL**: Now tracks real wallet balance change
2. **Exit thresholds**: Increased to 0.25 (from 0.08) to let trades run
3. **Entry threshold**: Increased to 0.20 (from 0.15) for higher quality
4. **TP removal**: Exchange TP removed (always failed), managed locally
5. **Breakeven stop**: Added at 50% of stop distance profit
6. **SWING mode**: Using 1h Supertrend, 60s interval, wider 3:1 RR targets

## Relevant Files
- `src/ETHWyckoffBot/Strategy/TFLadderStrategy.cs`: Swing entry/exit logic
- `src/ETHWyckoffBot/Strategy/TrailingStop.cs`: Breakeven + trailing stop
- `src/ETHWyckoffBot/Strategy/SignalFusionEngine.cs`: Score fusion
- `src/ETHWyckoffBot/Services/TradingEngine.cs`: Trade loop, real PnL tracking
- `src/ETHWyckoffBot/Exchange/DeltaConnector.cs`: Delta API client
- `src/ETHWyckoffBot/Risk/RiskManager.cs`: Risk limits
- `src/ETHWyckoffBot/appsettings.json`: Config (interval: 60s)
- `src/ETHWyckoffBot/Indicators/VWAP.cs`: VWAP indicator for mean reversion

## Build Command
```
cd C:\Users\DEEKSHITH\ETH-Wyckoff-Bot\src\ETHWyckoffBot; dotnet build
```

## Launch
Desktop shortcut created at: `C:\Users\DEEKSHITH\Desktop\ETHWyckoffBot.lnk`
EXE: `C:\Users\DEEKSHITH\ETH-Wyckoff-Bot\src\ETHWyckoffBot\bin\Debug\net10.0-windows\ETHWyckoffBot.exe`

## Full Session History (26 Jun 2026)

### Balance Progression (all time)
- Start: ~$159 (unknown exact initial)
- Best: ~$195
- Current: $127.01
- Goal: $200 profit (need ~$327 total)

### Root Cause Analysis of Losses
1. **Fake PnL**: Local PnL used candle close (not fill price). Showed +$4.27 profit but actual balance dropped **-$17.65** (slippage + fees)
2. **FIXED**: Real PnL now tracked via `_balanceBeforeEntry` vs post-exit balance
3. **Scalp vs Swing**: Scalping (1m, 0.30% TP) loses to slippage/fees on Delta testnet. **Switched to swing** (1h trend, 0.60% SL, 1.80% TP, 3:1 RR)

### Key Changes Made This Session
- TP: removed exchange placement (always fails), managed locally
- SL/TP: fixed 0.60%/1.80% percentages (was ATR-based 0.30% min)
- Entry threshold: 0.12 → 0.18 → 0.20 (higher quality)
- Exit threshold: 0.08 → 0.15 → 0.25 (let trades run)
- AMD weight: 0.35 → 0.15 (when Unknown, doesn't kill fusion)
- OF weight: 0.20 → 0.30 (more weight on order flow)
- VP weight: 0.20 → 0.30 (more weight on value area)
- Risk: 3%/$5 cap → 5%/$10 cap (bigger positions)
- Interval: 15s → 60s (less API pressure)
- Check interval: 15s → 60s (swing cadence)
- Strategy: Pure fusion scalping → VWAP + 1h Supertrend swing
- PnL tracking: Candle close → Real wallet balance diff
- Breakeven: Added at 50% of stop distance
- SL sizing: Uses max(ATR*2.5, price*0.003) → Uses fixed price*0.006
- Desktop shortcut created

### Code Structure
```
ETH-Wyckoff-Bot/
├── AGENTS.md (this file)
├── ETHWyckoffBot.sln
├── src/
│   └── ETHWyckoffBot/
│       ├── ETHWyckoffBot.csproj
│       ├── appsettings.json
│       ├── Program.cs
│       ├── App.xaml / MainWindow.xaml
│       ├── Exchange/
│       │   └── DeltaConnector.cs
│       ├── Indicators/
│       │   ├── Supertrend.cs
│       │   ├── VWAP.cs
│       │   └── VolumeProfile.cs
│       ├── Models/
│       │   ├── TradeRecord.cs
│       │   └── ...
│       ├── Risk/
│       │   └── RiskManager.cs
│       ├── Services/
│       │   └── TradingEngine.cs
│       └── Strategy/
│           ├── TFLadderStrategy.cs (swing logic)
│           ├── SignalFusionEngine.cs
│           ├── TrailingStop.cs (breakeven + trailing)
│           ├── EntryFilters.cs
│           └── AMDDetector.cs
```

### Build & Run
```
cd C:\Users\DEEKSHITH\ETH-Wyckoff-Bot\src\ETHWyckoffBot
dotnet build
dotnet run
```
Or double-click desktop shortcut `ETHWyckoffBot.lnk`
