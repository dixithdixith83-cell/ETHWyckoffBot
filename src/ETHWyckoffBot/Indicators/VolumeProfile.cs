using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Indicators;

public class VolumeProfile
{
    private readonly int _numZones;
    private readonly Dictionary<decimal, decimal> _volumeAtPrice = new();
    private decimal _highestPrice;
    private decimal _lowestPrice;
    private decimal _totalVolume;
    private decimal _valueAreaHigh;
    private decimal _valueAreaLow;
    private decimal _pointOfControl;
    private int _candlesProcessed;
    private decimal _cumulativeTPV;

    public decimal ValueAreaHigh => _valueAreaHigh;
    public decimal ValueAreaLow => _valueAreaLow;
    public decimal PointOfControl => _pointOfControl;
    public decimal VWAP => _cumulativeTPV > 0 ? _cumulativeTPV / _totalVolume : 0;
    public decimal TotalVolume => _totalVolume;
    public int CandlesProcessed => _candlesProcessed;
    public bool IsReady => _candlesProcessed >= 10;

    public VolumeProfile(int numZones = 12)
    {
        _numZones = numZones;
    }

    public void AddCandle(Candle candle)
    {
        _candlesProcessed++;
        var zoneSize = CalculateZoneSize(candle.High, candle.Low);

        if (zoneSize <= 0) return;

        var price = candle.Low;
        while (price <= candle.High)
        {
            var zone = Math.Floor(price / zoneSize) * zoneSize;
            _volumeAtPrice.TryGetValue(zone, out var currentVol);
            _volumeAtPrice[zone] = currentVol + candle.Volume / ((candle.High - candle.Low) / zoneSize + 1);
            price += zoneSize;
        }

        _totalVolume += candle.Volume;
        _cumulativeTPV += ((candle.High + candle.Low + candle.Close) / 3) * candle.Volume;
        if (candle.High > _highestPrice) _highestPrice = candle.High;
        if (candle.Low > 0 && (_lowestPrice == 0 || candle.Low < _lowestPrice)) _lowestPrice = candle.Low;

        if (_candlesProcessed >= 10)
            Recalculate();
    }

    private decimal CalculateZoneSize(decimal high, decimal low)
    {
        var range = high - low;
        if (range <= 0) return 0;
        return Math.Max(range / _numZones, 0.01m);
    }

    private void Recalculate()
    {
        if (_volumeAtPrice.Count == 0) return;

        var sorted = _volumeAtPrice.OrderByDescending(kvp => kvp.Value).ToList();
        _pointOfControl = sorted[0].Key;

        var total = sorted.Sum(kvp => kvp.Value);
        var target70 = total * 0.70m;
        var running = 0m;
        var pocIndex = sorted.FindIndex(kvp => kvp.Key == _pointOfControl);

        var included = new HashSet<decimal> { _pointOfControl };
        running += sorted[pocIndex].Value;

        var up = pocIndex + 1;
        var down = pocIndex - 1;

        while (running < target70 && (up < sorted.Count || down >= 0))
        {
            var upVal = up < sorted.Count ? sorted[up].Value : -1;
            var downVal = down >= 0 ? sorted[down].Value : -1;

            if (upVal >= downVal && up < sorted.Count)
            {
                included.Add(sorted[up].Key);
                running += sorted[up].Value;
                up++;
            }
            else if (down >= 0)
            {
                included.Add(sorted[down].Key);
                running += sorted[down].Value;
                down--;
            }
            else break;
        }

        _valueAreaHigh = included.Max();
        _valueAreaLow = included.Min();
    }

    public decimal GetDistanceFromVA(decimal price)
    {
        if (!IsReady) return 0;
        if (price > _valueAreaHigh) return (price - _valueAreaHigh) / (_valueAreaHigh - _valueAreaLow + 0.01m);
        if (price < _valueAreaLow) return (price - _valueAreaLow) / (_valueAreaHigh - _valueAreaLow + 0.01m);
        return 0;
    }

    public bool IsPriceInValueArea(decimal price) => IsReady && price >= _valueAreaLow && price <= _valueAreaHigh;

    public double GetScore(decimal currentPrice, TradeDirection direction)
    {
        if (!IsReady) return 0;

        if (direction == TradeDirection.Long)
        {
            if (currentPrice <= _valueAreaLow) return 0.8;
            if (currentPrice <= _pointOfControl) return 0.4;
            if (currentPrice >= _valueAreaHigh) return -0.4;
            return 0;
        }
        else
        {
            if (currentPrice >= _valueAreaHigh) return 0.8;
            if (currentPrice >= _pointOfControl) return 0.4;
            if (currentPrice <= _valueAreaLow) return -0.4;
            return 0;
        }
    }

    public void Reset()
    {
        _volumeAtPrice.Clear();
        _highestPrice = 0;
        _lowestPrice = 0;
        _totalVolume = 0;
        _valueAreaHigh = 0;
        _valueAreaLow = 0;
        _pointOfControl = 0;
        _candlesProcessed = 0;
        _cumulativeTPV = 0;
    }
}
