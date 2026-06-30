namespace ETHWyckoffBot.Indicators;

public class EMA
{
    private readonly int _period;
    private decimal _multiplier;
    private bool _isInitialized;

    public EMA(int period)
    {
        _period = period;
        _multiplier = 2.0m / (period + 1);
    }

    public decimal Value { get; private set; }

    public void Update(decimal price)
    {
        if (!_isInitialized)
        {
            Value = price;
            _isInitialized = true;
            return;
        }

        Value = ((price - Value) * _multiplier) + Value;
    }

    public void Reset()
    {
        _isInitialized = false;
        Value = 0;
    }
}
