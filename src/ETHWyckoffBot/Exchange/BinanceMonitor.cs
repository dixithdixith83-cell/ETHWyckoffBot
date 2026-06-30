using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Exchange;

public class BinanceMonitor : IDisposable
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;

    public event Action<decimal, decimal, bool>? LargeTradeDetected;
    public event Action<List<(decimal Price, decimal Volume)>, List<(decimal Price, decimal Volume)>>? OrderBookUpdated;

    public async Task StartAsync(string symbol = "ethusdt")
    {
        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri($"wss://stream.binance.com:9443/ws/{symbol}@depth20@100ms"), _cts.Token);

        _ = Task.Run(async () =>
        {
            var buffer = new byte[65536];
            while (_ws?.State == WebSocketState.Open)
            {
                var result = await _ws.ReceiveAsync(buffer, _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                ProcessDepthMessage(msg);
            }
        });

        var ws2 = new ClientWebSocket();
        await ws2.ConnectAsync(new Uri($"wss://stream.binance.com:9443/ws/{symbol}@aggTrade"), _cts.Token);

        _ = Task.Run(async () =>
        {
            var buffer = new byte[65536];
            while (ws2.State == WebSocketState.Open)
            {
                var result = await ws2.ReceiveAsync(buffer, _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                ProcessTradeMessage(msg);
            }
        });
    }

    private void ProcessDepthMessage(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var bids = new List<(decimal, decimal)>();
            var asks = new List<(decimal, decimal)>();

            foreach (var b in doc.RootElement.GetProperty("bids").EnumerateArray())
                bids.Add((decimal.Parse(b[0].GetString()!), decimal.Parse(b[1].GetString()!)));
            foreach (var a in doc.RootElement.GetProperty("asks").EnumerateArray())
                asks.Add((decimal.Parse(a[0].GetString()!), decimal.Parse(a[1].GetString()!)));

            OrderBookUpdated?.Invoke(bids, asks);
        }
        catch { }
    }

    private void ProcessTradeMessage(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var price = decimal.Parse(doc.RootElement.GetProperty("p").GetString()!);
            var qty = decimal.Parse(doc.RootElement.GetProperty("q").GetString()!);
            var isBuyer = doc.RootElement.GetProperty("m").GetBoolean();
            var usdValue = price * qty;

            if (usdValue >= 100000m)
                LargeTradeDetected?.Invoke(price, qty, isBuyer);
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
