using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Exchange;

public class BybitMonitor : IDisposable
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;

    public event Action<decimal, decimal, bool>? LargeTradeDetected;
    public event Action<List<(decimal Price, decimal Volume)>, List<(decimal Price, decimal Volume)>>? OrderBookUpdated;

    public async Task StartAsync(string symbol = "ETHUSDT")
    {
        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri("wss://stream.bybit.com/v5/public/linear"), _cts.Token);

        var sub = JsonSerializer.Serialize(new
        {
            op = "subscribe",
            args = new[] { $"orderbook.50.{symbol}", $"publicTrade.{symbol}" }
        });
        await _ws.SendAsync(Encoding.UTF8.GetBytes(sub), WebSocketMessageType.Text, true, _cts.Token);

        _ = Task.Run(async () =>
        {
            var buffer = new byte[65536];
            while (_ws?.State == WebSocketState.Open)
            {
                var result = await _ws.ReceiveAsync(buffer, _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                ProcessMessage(msg);
            }
        });
    }

    private void ProcessMessage(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("type", out var type)) return;
            var topic = doc.RootElement.TryGetProperty("topic", out var t) ? t.GetString() : "";

            if (topic?.Contains("orderbook") == true)
            {
                var data = doc.RootElement.GetProperty("data");
                var bids = new List<(decimal, decimal)>();
                var asks = new List<(decimal, decimal)>();
                foreach (var b in data.GetProperty("b").EnumerateArray())
                    bids.Add((decimal.Parse(b[0].GetString()!), decimal.Parse(b[1].GetString()!)));
                foreach (var a in data.GetProperty("a").EnumerateArray())
                    asks.Add((decimal.Parse(a[0].GetString()!), decimal.Parse(a[1].GetString()!)));
                OrderBookUpdated?.Invoke(bids, asks);
            }
            else if (topic?.Contains("publicTrade") == true)
            {
                var data = doc.RootElement.GetProperty("data");
                foreach (var trade in data.EnumerateArray())
                {
                    var price = decimal.Parse(trade.GetProperty("p").GetString()!);
                    var qty = decimal.Parse(trade.GetProperty("v").GetString()!);
                    var side = trade.GetProperty("S").GetString();
                    var usdValue = price * qty;
                    if (usdValue >= 100000m)
                        LargeTradeDetected?.Invoke(price, qty, side == "Buy");
                }
            }
        }
        catch { }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _ws?.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }
}
