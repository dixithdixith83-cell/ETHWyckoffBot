using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Services;

public class WhaleTrackerService
{
    private readonly EthOnChainClient _ethClient;
    private readonly BtcBlockchainClient _btcClient;
    private readonly List<WhaleAlert> _recentAlerts = new();
    private readonly int _minValueUsd;

    public decimal AccumulationScore { get; private set; }
    public decimal DistributionScore { get; private set; }
    public IReadOnlyList<WhaleAlert> RecentAlerts => _recentAlerts.AsReadOnly();

    public event Action<WhaleAlert>? WhaleAlertRaised;

    public WhaleTrackerService(EthOnChainClient ethClient, BtcBlockchainClient btcClient, int minValueUsd = 100000)
    {
        _ethClient = ethClient;
        _btcClient = btcClient;
        _minValueUsd = minValueUsd;
    }

    public async Task EvaluateAsync()
    {
        try
        {
            var ethTxs = await _ethClient.GetWhaleTransactionsAsync();
            var btcTxs = await _btcClient.GetWhaleTransactionsAsync();

            foreach (var tx in ethTxs.Concat(btcTxs))
            {
                _recentAlerts.Insert(0, tx);
                WhaleAlertRaised?.Invoke(tx);
            }

            while (_recentAlerts.Count > 50)
                _recentAlerts.RemoveAt(_recentAlerts.Count - 1);

            var buys = _recentAlerts.Count(a => a.Type == WhaleAlertType.WhaleBuy || a.Type == WhaleAlertType.ExchangeOutflow);
            var sells = _recentAlerts.Count(a => a.Type == WhaleAlertType.WhaleSell || a.Type == WhaleAlertType.ExchangeInflow);
            var total = buys + sells;

            if (total > 0)
            {
                AccumulationScore = Math.Clamp((decimal)buys / total, 0, 1);
                DistributionScore = Math.Clamp((decimal)sells / total, 0, 1);
            }
        }
        catch { }
    }

    public double GetScore()
    {
        return (double)(AccumulationScore - DistributionScore);
    }

    public void Reset()
    {
        _recentAlerts.Clear();
        AccumulationScore = 0;
        DistributionScore = 0;
    }
}
