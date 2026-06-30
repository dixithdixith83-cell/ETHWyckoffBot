using System.Net.Http;
using System.Text.Json;
using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Services;

public class BtcBlockchainClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly int _minValueUsd;

    public BtcBlockchainClient(string baseUrl = "https://blockchain.info", int minValueUsd = 100000)
    {
        _baseUrl = baseUrl;
        _minValueUsd = minValueUsd;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<List<WhaleAlert>> GetWhaleTransactionsAsync()
    {
        var alerts = new List<WhaleAlert>();
        try
        {
            var latestJson = await _http.GetStringAsync($"{_baseUrl}/latestblock");
            var latest = JsonDocument.Parse(latestJson);
            var blockHash = latest.RootElement.GetProperty("hash").GetString();
            if (blockHash == null) return alerts;

            var blockJson = await _http.GetStringAsync($"{_baseUrl}/rawblock/{blockHash}");
            var block = JsonDocument.Parse(blockJson);
            if (!block.RootElement.TryGetProperty("tx", out var txs)) return alerts;

            foreach (var tx in txs.EnumerateArray().Take(50))
            {
                if (!tx.TryGetProperty("out", out var outputs)) continue;

                foreach (var output in outputs.EnumerateArray())
                {
                    var valueSat = output.TryGetProperty("value", out var v) ? v.GetInt64() : 0;
                    if (valueSat <= 0) continue;

                    var btcValue = valueSat / 1e8m;
                    var usdValue = btcValue * 60000m; // approximate BTC price
                    if (usdValue < _minValueUsd) continue;

                    var addr = output.TryGetProperty("addr", out var a) ? a.GetString() : "";
                    var hash = tx.TryGetProperty("hash", out var h) ? h.GetString() : "";

                    alerts.Add(new WhaleAlert
                    {
                        Timestamp = DateTime.UtcNow,
                        Title = $"BTC Whale: {btcValue:F4} BTC (${usdValue:N0})",
                        Description = $"To: {(addr != null && addr.Length > 12 ? addr[..12] : "?")}... | {(hash != null && hash.Length > 10 ? hash[..10] : "?")}...",
                        ValueUsd = usdValue,
                        Type = WhaleAlertType.WhaleBuy
                    });
                }
            }
        }
        catch { }
        return alerts;
    }
}
