using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Risk;

public class RiskManager
{
    private readonly AppConfig _config;
    private decimal _peakBalance;
    private decimal _dayStartBalance;
    private DateTime _lastDayReset;
    private int _consecutiveLosses;
    private bool _halted;
    private bool _initialized;

    public bool IsHalted => _halted;
    public string? HaltReason { get; private set; }
    public decimal CurrentDrawdown { get; private set; }
    public decimal CurrentPositionSizeMultiplier { get; private set; } = 1.0m;

    public RiskManager(AppConfig config)
    {
        _config = config;
        _peakBalance = 0;
        _dayStartBalance = 0;
        _lastDayReset = DateTime.UtcNow.Date;
    }

    public RiskCheckResult Check(decimal balance, TradeRecord? lastTrade)
    {
        if (_halted)
        {
            if (balance > 0 && DateTime.UtcNow.Date != _lastDayReset)
            {
                _halted = false;
                HaltReason = null;
                _consecutiveLosses = 0;
                _dayStartBalance = balance;
                _lastDayReset = DateTime.UtcNow.Date;
            }
            if (_halted)
                return new RiskCheckResult { Approved = false, Reason = HaltReason ?? "Trading halted" };
        }

        if (!_initialized && balance > 0)
        {
            _peakBalance = balance;
            _dayStartBalance = balance;
            _initialized = true;
        }

        if (balance <= 0)
            return new RiskCheckResult { Approved = false, Reason = "Zero balance" };

        if (DateTime.UtcNow.Date != _lastDayReset)
        {
            _dayStartBalance = balance;
            _lastDayReset = DateTime.UtcNow.Date;
            _consecutiveLosses = 0;
        }

        if (balance > _peakBalance)
            _peakBalance = balance;

        CurrentDrawdown = _peakBalance > 0 ? (_peakBalance - balance) / _peakBalance * 100 : 0;
        if (_peakBalance > 0 && (double)CurrentDrawdown >= (double)_config.Trading.MaxDrawdownPercent * 100)
        {
            _halted = true;
            HaltReason = $"Max drawdown {CurrentDrawdown:F1}% exceeded";
            return new RiskCheckResult { Approved = false, Reason = HaltReason };
        }

        var dailyLoss = _dayStartBalance > 0 ? (_dayStartBalance - balance) / _dayStartBalance * 100 : 0;
        if (_dayStartBalance > 0 && (double)dailyLoss >= (double)_config.Trading.MaxDailyLossPercent * 100)
        {
            _halted = true;
            HaltReason = $"Daily loss {dailyLoss:F1}% exceeded";
            return new RiskCheckResult { Approved = false, Reason = HaltReason };
        }

        if (lastTrade != null && lastTrade.PnL < 0)
        {
            _consecutiveLosses++;
            var maxLosses = _config.Trading.MaxConsecutiveLosses;
            if (_consecutiveLosses >= maxLosses)
            {
                _halted = true;
                HaltReason = $"{maxLosses} consecutive losses ({_consecutiveLosses}) — re-evaluate";
                return new RiskCheckResult { Approved = false, Reason = HaltReason };
            }
        }
        else if (lastTrade != null && lastTrade.PnL > 0)
        {
            _consecutiveLosses = 0;
            CurrentPositionSizeMultiplier = Math.Min(CurrentPositionSizeMultiplier + 0.05m, 0.5m);
        }

        // Conservative: max 2% of balance per position
        var maxSize = balance * 0.02m * CurrentPositionSizeMultiplier;
        return new RiskCheckResult { Approved = true, MaxPositionValue = maxSize };
    }

    public void Resume()
    {
        _halted = false;
        HaltReason = null;
        _consecutiveLosses = 0;
        CurrentPositionSizeMultiplier = 1.0m;
    }
}

public class RiskCheckResult
{
    public bool Approved { get; set; }
    public string? Reason { get; set; }
    public decimal MaxPositionValue { get; set; }
}
