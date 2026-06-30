using System.Net.Http;
using System.Text.Json;
using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Services;

public class EtherscanClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private const string BaseUrl = "https://api.etherscan.io/api";

    public EtherscanClient(string apiKey = "")
    {
        _apiKey = apiKey;
        _http = new HttpClient();
    }

    public async Task<List<WhaleAlert>> GetLargeTransactionsAsync(int minValueUsd = 100000)
    {
        var alerts = new List<WhaleAlert>();
        try
        {
            var url = $"{BaseUrl}?module=account&action=txlist&address=0x742d35Cc6634C0532925a3b844Bc9e7595f0bEb0&sort=desc&apikey={_apiKey}";
            var json = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var result))
            {
                foreach (var tx in result.EnumerateArray().Take(20))
                {
                    var valueWei = tx.TryGetProperty("value", out var v) ? v.GetString() : "0";
                    if (decimal.TryParse(valueWei, out var wei) && wei > 0)
                    {
                        var ethValue = wei / 1e18m;
                        var usdValue = ethValue * 3000m;
                        if (usdValue >= minValueUsd)
                        {
                            alerts.Add(new WhaleAlert
                            {
                                Timestamp = DateTime.UtcNow,
                                Title = $"Whale tx: {ethValue:F2} ETH",
                                Description = $"${usdValue:N0} | {tx.GetProperty("hash").GetString()?[..10]}...",
                                ValueUsd = usdValue,
                                Type = WhaleAlertType.WhaleBuy
                            });
                        }
                    }
                }
            }
        }
        catch { }
        return alerts;
    }
}
