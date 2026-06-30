using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Services;

public class EthOnChainClient
{
    private readonly HttpClient _http;
    private readonly string _rpcUrl;
    private readonly int _minValueUsd;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public EthOnChainClient(string rpcUrl, int minValueUsd = 100000)
    {
        _rpcUrl = rpcUrl;
        _minValueUsd = minValueUsd;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<List<WhaleAlert>> GetWhaleTransactionsAsync()
    {
        var alerts = new List<WhaleAlert>();
        try
        {
            var blockNumHex = await CallRpcAsync<string>("eth_blockNumber");
            if (blockNumHex == null) return alerts;

            var block = await CallRpcAsync<JsonElement>("eth_getBlockByNumber", blockNumHex, true);
            if (block.ValueKind == JsonValueKind.Undefined) return alerts;

            if (!block.TryGetProperty("transactions", out var txs)) return alerts;

            foreach (var tx in txs.EnumerateArray())
            {
                var valueHex = tx.TryGetProperty("value", out var v) ? v.GetString() : "0x0";
                var valueWei = ParseHexBigInt(valueHex);
                if (valueWei <= 0) continue;

                var ethValue = valueWei / 1e18m;
                var usdValue = ethValue * 3000m; // approximate ETH price
                if (usdValue < _minValueUsd) continue;

                var from = tx.TryGetProperty("from", out var f) ? f.GetString() : "";
                var to = tx.TryGetProperty("to", out var t) ? t.GetString() : "";
                var hash = tx.TryGetProperty("hash", out var h) ? h.GetString() : "";

                var type = DetermineWhaleType(from ?? "", to ?? "");
                alerts.Add(new WhaleAlert
                {
                    Timestamp = DateTime.UtcNow,
                    Title = $"ETH Whale: {ethValue:F2} ETH (${usdValue:N0})",
                    Description = $"From: {(from != null && from.Length > 8 ? from[..8] : "?")}... → To: {(to != null && to.Length > 8 ? to[..8] : "?")}... | {(hash != null && hash.Length > 10 ? hash[..10] : "?")}...",
                    ValueUsd = usdValue,
                    Type = type
                });
            }
        }
        catch { }
        return alerts;
    }

    private async Task<T?> CallRpcAsync<T>(string method, params object[]? parameters)
    {
        var request = new { jsonrpc = "2.0", method, @params = parameters ?? Array.Empty<object>(), id = 1 };
        var response = await _http.PostAsJsonAsync(_rpcUrl, request, JsonOpts);
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (doc.TryGetProperty("result", out var result))
            return JsonSerializer.Deserialize<T>(result.GetRawText());
        return default;
    }

    private static decimal ParseHexBigInt(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || hex == "0x0") return 0;
        hex = hex!.StartsWith("0x") ? hex[2..] : hex;
        if (hex.Length == 0) return 0;
        try { return (decimal)System.Numerics.BigInteger.Parse(hex, System.Globalization.NumberStyles.HexNumber); }
        catch { return 0; }
    }

    private static WhaleAlertType DetermineWhaleType(string from, string to)
    {
        var exchanges = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "0x742d35cc6634c0532925a3b844bc9e7595f0beb0", "0x28c6c06298d514db089934071355e5743bf21d60",
            "0x21a31ee1afc51d94c2efccaa2092ad1028285549", "0x3f5ce5fbfe3e9af3971dd833d26ba9b5c936f0be"
        };

        if (exchanges.Contains(to)) return WhaleAlertType.ExchangeInflow;
        if (exchanges.Contains(from)) return WhaleAlertType.ExchangeOutflow;
        return WhaleAlertType.WhaleBuy;
    }
}
