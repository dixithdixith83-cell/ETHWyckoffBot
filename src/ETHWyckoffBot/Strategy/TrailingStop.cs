using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Strategy;

public class TrailingStop
{
    private decimal _entryPrice;
    private decimal _highestPrice;
    private decimal _lowestPrice;
    private decimal _stopDistance;
    private decimal _targetDistance;

    public decimal CurrentStop { get; private set; }
    public decimal TakeProfit { get; private set; }
    public TradeDirection Direction { get; private set; }
    public decimal EntryPrice => _entryPrice;
    public decimal StopDistance => _stopDistance;

    public void Initialize(decimal entryPrice, TradeDirection direction, decimal stopDistance, decimal targetDistance)
    {
        _entryPrice = entryPrice;
        _stopDistance = Math.Max(stopDistance, entryPrice * 0.003m);
        _targetDistance = Math.Max(targetDistance, _stopDistance);
        Direction = direction;

        _highestPrice = entryPrice;
        _lowestPrice = entryPrice;

        if (direction == TradeDirection.Long)
        {
            CurrentStop = entryPrice - _stopDistance;
            TakeProfit = entryPrice + _targetDistance;
        }
        else
        {
            CurrentStop = entryPrice + _stopDistance;
            TakeProfit = entryPrice - _targetDistance;
        }
    }

    public void Update(decimal currentPrice)
    {
        if (Direction == TradeDirection.Long)
        {
            if (currentPrice > _highestPrice)
            {
                _highestPrice = currentPrice;
                var newStop = currentPrice - _stopDistance;
                if (newStop > CurrentStop)
                    CurrentStop = newStop;
                // Breakeven: move to entry once price moves 50% of target in profit
                if (_highestPrice >= _entryPrice + _targetDistance * 0.5m && CurrentStop < _entryPrice)
                    CurrentStop = _entryPrice;
            }
        }
        else
        {
            if (currentPrice < _lowestPrice || _lowestPrice == 0)
            {
                _lowestPrice = currentPrice;
                var newStop = currentPrice + _stopDistance;
                if (newStop < CurrentStop || CurrentStop == 0)
                    CurrentStop = newStop;
                // Breakeven: move to entry once price moves 50% of target in profit
                if (_lowestPrice <= _entryPrice - _targetDistance * 0.5m && (CurrentStop > _entryPrice || CurrentStop == 0))
                    CurrentStop = _entryPrice;
            }
        }
    }

    public (bool stopHit, bool tpHit) CheckExits(decimal currentPrice)
    {
        if (Direction == TradeDirection.Long)
        {
            return (currentPrice <= CurrentStop, currentPrice >= TakeProfit);
        }
        else
        {
            return (currentPrice >= CurrentStop, currentPrice <= TakeProfit);
        }
    }

    public void Reset()
    {
        _entryPrice = 0;
        _highestPrice = 0;
        _lowestPrice = 0;
        _stopDistance = 0;
        CurrentStop = 0;
        TakeProfit = 0;
        Direction = TradeDirection.None;
    }
}
