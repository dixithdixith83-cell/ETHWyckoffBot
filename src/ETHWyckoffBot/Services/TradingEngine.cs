using ETHWyckoffBot.Exchange;
using ETHWyckoffBot.Indicators;
using ETHWyckoffBot.Models;
using ETHWyckoffBot.Risk;
using ETHWyckoffBot.Strategy;
using Serilog;
using WhaleAlertModel = ETHWyckoffBot.Models.WhaleAlert;

namespace ETHWyckoffBot.Services;

public class TradingEngine : IDisposable
{
    private readonly IExchangeConnector _connector;
    private readonly AppConfig _config;
    private readonly WhaleTrackerService _whaleTracker;
    private readonly RiskManager _risk;
    private readonly NotificationService _notifications;
    private readonly MetricsCollector _metrics;
    private BinanceMonitor? _binanceMon;
    private BybitMonitor? _bybitMon;
    private bool _connectivityAlertSent;

    private readonly Dictionary<string, SymbolState> _symbols = new();
    private ProcessCrashMonitor? _crashMonitor;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private bool _isRunning;

    public IReadOnlyDictionary<string, SymbolState> Symbols => _symbols;
    public bool IsRunning => _isRunning;
    public WhaleTrackerService WhaleTracker => _whaleTracker;
    public RiskManager Risk => _risk;
    public NotificationService Notifications => _notifications;
    public MetricsCollector Metrics => _metrics;

    public event Action<string>? LogMessage;
    public event Action<decimal>? BalanceUpdated;

    public TradingEngine(
        IExchangeConnector connector,
        AppConfig config,
        WhaleTrackerService whaleTracker,
        RiskManager risk,
        NotificationService notifications,
        MetricsCollector metrics)
    {
        _connector = connector;
        _config = config;
        _whaleTracker = whaleTracker;
        _risk = risk;
        _notifications = notifications;
        _metrics = metrics;

        foreach (var sym in config.Delta.Symbols)
        {
            var amd = new AMDDetector(new CumulativeVolumeDelta(), new OrderBookImbalance());
            var stopHunt = new StopHuntDetector();
            var cvd = new CumulativeVolumeDelta();
            var ob = new OrderBookImbalance();
            var strategy = new TFLadderStrategy(config.Ladder, config.Trading);
            var vp = new VolumeProfile(12);
            var vwap = new VWAP();
            var filters = new EntryFilters(config.Trading, config.Ladder)
            {
                VolumeProfile = vp,
                Vwap = vwap,
                CVD = cvd
            };
            var fusion = new SignalFusionEngine(amd, cvd, ob, strategy, filters, stopHunt, config);

            strategy.VolumeProfile = vp;
            strategy.Vwap = vwap;
            strategy.Filters = filters;
            strategy.SetAMD(amd);
            strategy.SetStopHunt(stopHunt);

            _symbols[sym] = new SymbolState
            {
                Symbol = sym,
                Strategy = strategy,
                AMD = amd,
                CVD = cvd,
                OrderBook = ob,
                StopHunt = stopHunt,
                VolumeProfile = vp,
                Vwap = vwap,
                Fusion = fusion,
                CandleCache = new CandleCache(200),
                TrailingStop = new TrailingStop()
            };

            Log($"Initialized symbol: {sym}");
        }
    }

    public void SetWhaleTrackerRef(WhaleTrackerServiceRef whaleRef)
    {
        foreach (var state in _symbols.Values)
            state.Fusion!.SetWhaleTracker(whaleRef);
    }

    public async Task<string> TestConnectionAsync()
    {
        return await _connector.TestConnectionAsync();
    }

    public async Task StartAsync()
    {
        if (_isRunning) return;
        _isRunning = true;

        _crashMonitor = new ProcessCrashMonitor();
        _crashMonitor.Start();

        if (_crashMonitor.WasUncleanShutdown)
            Log("*** PREVIOUS CRASH DETECTED — recovering trades ***");

        _risk.Resume();
        Log("Risk manager resumed on start");
        Log("Trading engine started");

        var firstSym = _config.Delta.Symbols[0];
        try { await _connector.SetLeverageAsync(firstSym, _config.Delta.Leverage); Log($"Leverage set to {_config.Delta.Leverage}x"); }
        catch (Exception ex) { Log($"Leverage setting failed: {ex.Message}"); }

        try
        {
            foreach (var state in _symbols.Values)
                await FetchInitialData(state);
        }
        catch (Exception ex) { Log($"Initial data fetch failed: {ex.Message}"); }

        try { await SyncPositionsAsync(); }
        catch (Exception ex) { Log($"Position sync failed: {ex.Message}"); }

        try { await StartMonitors(); }
        catch (Exception ex) { Log($"Monitor start failed: {ex.Message}"); }

        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => MainLoop(_loopCts.Token));

        try { await SubscribeToRealtime(firstSym); }
        catch (Exception ex) { Log($"Realtime subscription failed: {ex.Message}"); }
    }

    public void Stop()
    {
        _isRunning = false;
        _loopCts?.Cancel();
        _binanceMon?.Stop();
        _bybitMon?.Stop();
        _crashMonitor?.Stop();
        Log("Trading engine stopped");
    }

    private async Task FetchInitialData(SymbolState state)
    {
        foreach (var tf in _config.Ladder.Timeframes)
        {
            var candles = await _connector.GetOHLCVAsync(state.Symbol, tf, 100);
            state.CandleCache.AddCandles(tf, candles);
            Log($"Loaded {candles.Count} candles for {state.Symbol} {tf}");
        }
    }

    private async Task SyncPositionsAsync()
    {
        foreach (var (sym, state) in _symbols)
        {
            try
            {
                var pos = await _connector.GetPositionAsync(sym);
                if (pos == null) continue;

                Log($"[{sym}] Found existing position: {pos.Direction} {pos.Quantity:F4} @ {pos.EntryPrice:F2}");

                await _connector.CancelAllOrdersAsync(sym);

                state.Position = pos;
                state.BalanceBeforeEntry = await _connector.GetBalanceAsync();

                var stopDist = pos.EntryPrice * (_config.Trading.StopLossPercent / 100m);
                var tpDist = pos.EntryPrice * (_config.Trading.TakeProfitPercent / 100m);
                state.TrailingStop.Initialize(pos.EntryPrice, pos.Direction, stopDist, tpDist);

                state.StopOrderId = await _connector.SetStopLossAsync(sym, state.TrailingStop.CurrentStop, pos.Direction, pos.Quantity);
                state.TpOrderId = await _connector.SetTakeProfitAsync(sym, state.TrailingStop.TakeProfit, pos.Direction, pos.Quantity);

                Log($"[{sym}] Position recovered. SL: {state.TrailingStop.CurrentStop:F2}, TP: {state.TrailingStop.TakeProfit:F2}");
            }
            catch (Exception ex)
            {
                Log($"[{sym}] Position sync error: {ex.Message}");
            }
        }
    }

    private async Task StartMonitors()
    {
        _binanceMon = new BinanceMonitor();
        _binanceMon.LargeTradeDetected += (price, qty, isBuy) =>
        {
            var usdVal = price * qty;
            if (usdVal >= 100000m)
            {
                var alert = new WhaleAlertModel
                {
                    Timestamp = DateTime.UtcNow,
                    Title = $"Binance whale: {qty:F2} ETH",
                    Description = isBuy ? "Buy" : "Sell",
                    ValueUsd = usdVal,
                    Type = isBuy ? WhaleAlertType.WhaleBuy : WhaleAlertType.WhaleSell
                };
                _notifications.Notify(NotificationType.Whale, alert.Title, alert.Description);
            }
        };
        await _binanceMon.StartAsync();

        _bybitMon = new BybitMonitor();
        await _bybitMon.StartAsync();

        _ = Task.Run(async () =>
        {
            while (_isRunning)
            {
                try { await _whaleTracker.EvaluateAsync(); }
                catch (Exception ex) { Log($"Whale eval error: {ex.Message}"); }
                await Task.Delay(300000);
            }
        });
    }

    private async Task SubscribeToRealtime(string symbol)
    {
        try
        {
            await _connector.SubscribeCandlesAsync(symbol, "1m", _ => { });
            await _connector.SubscribeTickerAsync(symbol, _ => { });
            _connectivityAlertSent = false;
        }
        catch (Exception ex)
        {
            if (!_connectivityAlertSent)
            {
                _notifications.Notify(NotificationType.Risk, "Connection Lost", $"Delta API connection failed: {ex.Message}");
                _connectivityAlertSent = true;
            }
        }
    }

    private async Task OnTimerTick()
    {
        if (!_isRunning) return;

        Log("=== TICK ===");
        try
        {
            var bal = await _connector.GetBalanceAsync();
            Log($"Balance: {bal:F2} USD");
            BalanceUpdated?.Invoke(bal);

            var riskCheck = _risk.Check(bal, null);
            if (!riskCheck.Approved)
            {
                Log($"Risk halted: {riskCheck.Reason}");
                foreach (var state in _symbols.Values)
                {
                    if (state.Position != null)
                        await ExecuteExit(state, riskCheck.Reason ?? "Risk");
                }
                return;
            }

            int activePositions = _symbols.Values.Count(s => s.Position != null);

            foreach (var state in _symbols.Values)
            {
                await ProcessSymbol(state, bal, activePositions);
            }
        }
        catch (Exception ex)
        {
            Log($"Error in timer tick: {ex.Message}");
            Log($"Stack: {ex.StackTrace?.Replace("\r\n", " | ")}");
        }
    }

    private async Task ProcessSymbol(SymbolState state, decimal balance, int activePositions)
    {
        try
        {
            foreach (var tf in _config.Ladder.Timeframes)
            {
                var candles = await _connector.GetOHLCVAsync(state.Symbol, tf, 2);
                if (candles.Count > 0)
                    state.CandleCache.AddCandle(tf, candles[^1]);
            }

            var latest = state.CandleCache.GetLatestAll();
            if (latest.Count == 0) return;

            if (latest.TryGetValue("1m", out var c1))
            {
                state.AMD!.Evaluate(c1);
                state.CVD.OnNewCandle();
            }

            var fusion = state.Fusion!.Fuse(latest);
            var action = state.Strategy!.Evaluate(latest, fusion);
            var currentPrice = latest.TryGetValue("1m", out var priceCandle) ? priceCandle.Close : await _connector.GetPriceAsync(state.Symbol);
            var amdPhase = state.AMD?.CurrentPhase.ToString() ?? "?";
            var vpReady = state.VolumeProfile?.IsReady == true;
            var vol = latest.TryGetValue("1m", out var volCandle) ? volCandle.Volume : 0;
            Log($"[{state.Symbol}] Action: {action}, Price: {currentPrice:F2}, Vol: {vol:F0}, AMD: {amdPhase}, VP: {(vpReady ? "ready" : "wait")}");
            Log($"  Fusion: {fusion?.Summary ?? "null"}");

            if (state.Position != null)
            {
                await UpdateTrailingStop(state, currentPrice);
                if (state.Position == null) return;
            }

            switch (action)
            {
                case TradingAction.EnterLong:
                case TradingAction.EnterShort:
                    if (activePositions < _config.Delta.MaxConcurrentPositions)
                    {
                        var dir = action == TradingAction.EnterLong ? TradeDirection.Long : TradeDirection.Short;
                        await ExecuteEntry(state, dir, currentPrice, fusion, balance);
                    }
                    else
                    {
                        Log($"[{state.Symbol}] Max concurrent positions ({_config.Delta.MaxConcurrentPositions}) reached — skipping entry");
                    }
                    break;
                case TradingAction.ExitLong:
                case TradingAction.ExitShort:
                    var exitReason = state.Strategy.LastExitReason ?? "Strategy";
                    await ExecuteExit(state, exitReason);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"[{state.Symbol}] Error: {ex.Message}");
        }
    }

    private async Task ExecuteEntry(SymbolState state, TradeDirection direction, decimal price, FusionResult? fusion, decimal balance)
    {
        state.BalanceBeforeEntry = balance;

        var stopPct = _config.Trading.StopLossPercent / 100m;
        var tpPct = _config.Trading.TakeProfitPercent / 100m;
        var stopDistance = price * stopPct;
        var targetDistance = price * tpPct;
        decimal contractSize = 0.01m;

        decimal maxMargin = balance * 0.60m;
        decimal maxPositionValue = maxMargin * _config.Delta.Leverage;
        decimal maxSizeByMargin = Math.Floor(maxPositionValue / price / contractSize) * contractSize;
        if (maxSizeByMargin < contractSize) maxSizeByMargin = contractSize;

        var riskPct = _config.Trading.RiskPerTradePercent / 100m;
        var riskCap = _config.Trading.MaxRiskPerTradeUSD;
        decimal riskAmount = Math.Min(balance * riskPct, riskCap);
        if (riskAmount < 0.5m) riskAmount = 0.5m;

        decimal rawSize = riskAmount / stopDistance;
        decimal positionSize = Math.Floor(rawSize / contractSize) * contractSize;
        if (positionSize < contractSize) positionSize = contractSize;
        if (positionSize > maxSizeByMargin) positionSize = maxSizeByMargin;

        var positionValue = positionSize * price;
        var actualRisk = positionSize * stopDistance;

        Log($"[{state.Symbol}] Balance: {balance:F2} USD | Leverage: {_config.Delta.Leverage}x | " +
            $"StopDist: {stopDistance:F2} ({_config.Trading.StopLossPercent:F2}%) | " +
            $"Risk: ${actualRisk:F2} | Pos: {positionSize:F4} (${positionValue:F2}) " +
            $"Margin: ${positionValue / _config.Delta.Leverage:F2}");

        try
        {
            bool success = false;
            for (int retry = 0; retry < 3; retry++)
            {
                success = await _connector.PlaceOrderAsync(state.Symbol, direction, positionSize, price);
                Log($"[{state.Symbol}] PlaceOrderAsync attempt {retry + 1}: success={success}");
                if (success) break;
                if (retry < 2) await Task.Delay(2000);
            }
            if (!success) { Log($"[{state.Symbol}] Order placement failed after 3 retries"); return; }
        }
        catch (Exception ex) { Log($"[{state.Symbol}] PlaceOrderAsync threw: {ex.GetType().Name}: {ex.Message}"); return; }

        state.Position = new Position
        {
            Direction = direction,
            EntryPrice = price,
            Quantity = positionSize,
            CurrentPrice = price,
            CurrentStop = direction == TradeDirection.Long ? price - stopDistance : price + stopDistance,
            LadderTier = 1,
            EntryTime = DateTime.UtcNow,
            ConfirmedTimeframes = new List<string> { "1m" }
        };

        state.TrailingStop.Initialize(price, direction, stopDistance, targetDistance);
        await Task.Delay(2000);

        try
        {
            state.StopOrderId = await _connector.SetStopLossAsync(state.Symbol, state.TrailingStop.CurrentStop, direction, positionSize);
            Log($"[{state.Symbol}] Stop loss placed at {state.TrailingStop.CurrentStop:F2} (ID: {state.StopOrderId})");
        }
        catch (Exception ex) { Log($"[{state.Symbol}] Stop loss order failed: {ex.Message}"); }

        try
        {
            state.TpOrderId = await _connector.SetTakeProfitAsync(state.Symbol, state.TrailingStop.TakeProfit, direction, positionSize);
            Log($"[{state.Symbol}] Take profit placed at {state.TrailingStop.TakeProfit:F2} (ID: {state.TpOrderId})");
        }
        catch (Exception ex) { Log($"[{state.Symbol}] Take profit order failed: {ex.Message}"); }

        var msg = $"ENTERED {state.Symbol} {direction} at {price:F2} | " +
            $"Size: {positionSize:F4} (${positionValue:F2}) | " +
            $"Risk: ${actualRisk:F2} | Stop: {state.TrailingStop.CurrentStop:F2} | " +
            $"Target: {state.TrailingStop.TakeProfit:F2} | Score: {fusion?.Confidence:F2}";
        Log($"[{state.Symbol}] {msg}");
        _notifications.Notify(NotificationType.Entry, $"Entry {state.Symbol} {direction}", msg);
    }

    private async Task ExecuteExit(SymbolState state, string reason)
    {
        if (state.Position == null) return;

        if (state.StopOrderId.HasValue)
        {
            try { await _connector.CancelOrderAsync(state.StopOrderId.Value); } catch { }
            state.StopOrderId = null;
        }
        if (state.TpOrderId.HasValue)
        {
            try { await _connector.CancelOrderAsync(state.TpOrderId.Value); } catch { }
            state.TpOrderId = null;
        }

        var livePos = await _connector.GetPositionAsync(state.Symbol);
        var realEntryPrice = livePos?.EntryPrice ?? state.Position.EntryPrice;
        var realQuantity = livePos?.Quantity ?? state.Position.Quantity;

        var success = await _connector.ClosePositionAsync(state.Symbol);
        if (!success) { Log($"[{state.Symbol}] Close position failed"); return; }

        await Task.Delay(1000);
        var newBalance = await _connector.GetBalanceAsync();
        var realPnL = newBalance - state.BalanceBeforeEntry;

        var trade = new TradeRecord
        {
            Direction = state.Position.Direction,
            EntryPrice = realEntryPrice,
            ExitPrice = livePos?.CurrentPrice ?? state.Position.CurrentPrice,
            Quantity = realQuantity,
            PnL = realPnL,
            PnLPercent = realEntryPrice > 0 ? (realPnL / (realEntryPrice * realQuantity)) * 100 : 0,
            EnteredAt = state.Position.EntryTime,
            ExitedAt = DateTime.UtcNow,
            ExitReason = reason
        };

        _metrics.RecordTrade(trade);
        state.Position = null;
        state.Strategy!.Reset();
        state.TrailingStop.Reset();
        state.BalanceBeforeEntry = 0;

        var msg = $"EXITED {state.Symbol} {trade.Direction} | REAL PnL: {trade.PnL:F2} ({trade.PnLPercent:F2}%) | Reason: {reason}";
        Log($"[{state.Symbol}] {msg}");
        _notifications.Notify(NotificationType.Exit, $"Exit {state.Symbol}", msg);
    }

    private async Task UpdateTrailingStop(SymbolState state, decimal currentPrice)
    {
        if (state.Position == null) return;

        var oldStop = state.TrailingStop.CurrentStop;
        state.Position.CurrentPrice = currentPrice;
        state.TrailingStop.Update(currentPrice);
        state.Position.CurrentStop = state.TrailingStop.CurrentStop;
        state.Position.LadderTier = state.Strategy!.CurrentTier;

        if (state.StopOrderId.HasValue && oldStop != state.TrailingStop.CurrentStop)
        {
            var minMove = state.Position.EntryPrice * 0.001m;
            if (Math.Abs(state.TrailingStop.CurrentStop - oldStop) >= minMove)
            {
                try
                {
                    await _connector.CancelOrderAsync(state.StopOrderId.Value);
                    state.StopOrderId = await _connector.SetStopLossAsync(
                        state.Symbol, state.TrailingStop.CurrentStop,
                        state.Position.Direction, state.Position.Quantity);
                    Log($"[{state.Symbol}] Trailing stop updated: {oldStop:F2} -> {state.TrailingStop.CurrentStop:F2}");
                }
                catch (Exception ex) { Log($"[{state.Symbol}] Stop update failed: {ex.Message}"); }
            }
        }

        var (stopHit, tpHit) = state.TrailingStop.CheckExits(currentPrice);
        if (stopHit)
            await ExecuteExit(state, $"Stop loss ({state.TrailingStop.CurrentStop:F2})");
        else if (tpHit)
            await ExecuteExit(state, $"Take profit ({state.TrailingStop.TakeProfit:F2})");
    }

    private async Task MainLoop(CancellationToken ct)
    {
        Log("Main loop started");
        var lastSuccessfulTick = DateTime.UtcNow;
        try
        {
            while (!ct.IsCancellationRequested && _isRunning)
            {
                await OnTimerTick();
                lastSuccessfulTick = DateTime.UtcNow;
                await Task.Delay(TimeSpan.FromSeconds(_config.Trading.CandleCheckIntervalSeconds), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"Main loop error: {ex.GetType().Name}: {ex.Message}");
            var downTime = DateTime.UtcNow - lastSuccessfulTick;
            if (downTime > TimeSpan.FromMinutes(2))
            {
                _notifications.Notify(NotificationType.Risk, "Connection Lost",
                    $"Exchange down for {downTime.TotalMinutes:F1} min, reconnecting...");
            }
        }
        Log("Main loop ended");
    }

    private void Log(string msg)
    {
        Serilog.Log.Information(msg);
        LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");
        try { System.IO.File.AppendAllText("C:\\Users\\DEEKSHITH\\logs\\heartbeat.txt", $"{DateTime.Now:HH:mm:ss} {msg}{Environment.NewLine}"); }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        _binanceMon?.Dispose();
        _bybitMon?.Dispose();
        (_connector as IDisposable)?.Dispose();
    }
}
