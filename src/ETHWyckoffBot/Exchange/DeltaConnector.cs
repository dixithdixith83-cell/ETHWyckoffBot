using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Exchange;

public class DeltaConnector : IExchangeConnector, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _wsUrl;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _wsCts;
    private Task? _wsReceiveTask;
    private readonly object _wsLock = new();

    private Action<Candle>? _onCandle;
    private Action<decimal>? _onPrice;
    private string? _tickerSubscriptionMessage;

    public string ExchangeName => "Delta Exchange";

    public DeltaConnector(string baseUrl, string apiKey, string apiSecret, string symbol = "ETH_USD")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _wsUrl = _baseUrl.Contains("testnet")
            ? "wss://testnet-socket.delta.exchange"
            : "wss://socket.india.delta.exchange";
        _apiKey = apiKey;
        _apiSecret = apiSecret;
        _configSymbol = symbol;
        _http = new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(60);
        _http.DefaultRequestHeaders.Add("api-key", _apiKey);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ETHWyckoffBot/1.0");
    }

    private string SignRequest(string method, string timestamp, string path, string query = "", string body = "")
    {
        var data = method + timestamp + path + query + body;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_apiSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLower();
    }

    private async Task<string> SendAuthenticatedAsync(HttpMethod method, string path, object? body = null)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var jsonBody = body != null ? JsonSerializer.Serialize(body) : "";
        var signature = SignRequest(method.Method, timestamp, path, "", jsonBody);

        var request = new HttpRequestMessage(method, $"{_baseUrl}{path}")
        {
            Content = body != null ? new StringContent(jsonBody, Encoding.UTF8, "application/json") : null
        };
        request.Headers.Add("api-key", _apiKey);
        request.Headers.Add("timestamp", timestamp);
        request.Headers.Add("signature", signature);

        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Delta API {method} {path} returned {response.StatusCode}: {content[..Math.Min(content.Length, 200)]}");
        return content;
    }

    public async Task<string> TestConnectionAsync()
    {
        try
        {
            // Use ticker endpoint (lightweight) instead of all products
            var sym = _configSymbol?.Replace("/", "_") ?? "ETH_USD";
            var url = $"{_baseUrl}/v2/tickers?symbols={sym}";
            var json = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var result) && result.GetArrayLength() > 0)
                return $"Connected. {sym} ticker: ${result[0].GetProperty("close").GetDecimal():F2}";
            return "Connected to Delta Exchange testnet.";
        }
        catch (Exception ex)
        {
            return $"Connection failed: {ex.Message}";
        }
    }

    private string? _configSymbol;

    public async Task<List<Candle>> GetOHLCVAsync(string symbol, string timeframe, int limit)
    {
        try
        {
            var resolution = timeframe switch
            {
                "1m" => "1m", "5m" => "5m", "10m" => "10m", "15m" => "15m",
                "1h" => "1h", "4h" => "4h", _ => "1m"
            };
            var resSec = timeframe switch
            {
                "1m" => 60, "5m" => 300, "10m" => 600, "15m" => 900,
                "1h" => 3600, "4h" => 14400, _ => 60
            };
            var end = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var start = end - resSec * limit;
            var sym = symbol.Replace("/", "_");
            var url = $"{_baseUrl}/v2/history/candles?symbol={sym}&resolution={resolution}&start={start}&end={end}";
            var json = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);

            var candles = new List<Candle>();
            if (doc.RootElement.TryGetProperty("result", out var result))
            {
                foreach (var item in result.EnumerateArray())
                {
                    candles.Add(new Candle
                    {
                        Timestamp = DateTimeOffset.FromUnixTimeSeconds(item.GetProperty("time").GetInt64()).UtcDateTime,
                        Open = item.GetProperty("open").GetDecimal(),
                        High = item.GetProperty("high").GetDecimal(),
                        Low = item.GetProperty("low").GetDecimal(),
                        Close = item.GetProperty("close").GetDecimal(),
                        Volume = item.GetProperty("volume").GetDecimal()
                    });
                }
            }
            return candles.OrderBy(c => c.Timestamp).ToList();
        }
        catch
        {
            return new List<Candle>();
        }
    }

    private async Task<long> GetProductIdAsync(string symbol)
    {
        var sym = symbol.Replace("/", "_");
        var json = await SendAuthenticatedAsync(HttpMethod.Get, $"/v2/products?symbols={sym}");
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("result", out var result))
        {
            foreach (var product in result.EnumerateArray())
            {
                var prodSymbol = product.GetProperty("symbol").GetString();
                if (prodSymbol == sym)
                    return product.GetProperty("id").GetInt64();
            }
        }
        throw new Exception($"Product {sym} not found on Delta Exchange");
    }

    public async Task<decimal> GetPriceAsync(string symbol)
    {
        var sym = symbol.Replace("/", "_");
        var url = $"{_baseUrl}/v2/tickers?symbols={sym}";
        var json = await _http.GetStringAsync(url);
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("result", out var result))
        {
            foreach (var ticker in result.EnumerateArray())
            {
                if (ticker.TryGetProperty("symbol", out var s) && s.GetString() == sym)
                    return ticker.GetProperty("close").GetDecimal();
            }
        }
        return 0;
    }

    public async Task<Position?> GetPositionAsync(string symbol)
    {
        var pid = await GetProductIdAsync(symbol);
        var json = await SendAuthenticatedAsync(HttpMethod.Get, $"/v2/positions?product_id={pid}");
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
        {
            var size = result.GetProperty("size").GetInt64();
            if (size == 0) return null;
            var entryPrice = decimal.Parse(result.GetProperty("entry_price").GetString()!);
            return new Position
            {
                Direction = size > 0 ? TradeDirection.Long : TradeDirection.Short,
                Quantity = Math.Abs(size) * 0.01m,
                EntryPrice = entryPrice,
                CurrentPrice = await GetPriceAsync(symbol)
            };
        }
        return null;
    }

    public async Task<bool> PlaceOrderAsync(string symbol, TradeDirection direction, decimal quantity, decimal price)
    {
        var pid = await GetProductIdAsync(symbol);
        var contractValue = 0.01m;
        var size = (int)(quantity / contractValue);
        if (size <= 0) size = 1;

        // Use limit order for entry to get maker fees (lower cost)
        // Place limit slightly aggressive to ensure fill
        var limitPrice = direction == TradeDirection.Long
            ? Math.Round(price * 1.0005m, 2) // 0.05% above market → fills fast
            : Math.Round(price * 0.9995m, 2); // 0.05% below market → fills fast

        var order = new
        {
            product_id = pid,
            size,
            side = direction == TradeDirection.Long ? "buy" : "sell",
            order_type = "limit_order",
            price = limitPrice,
            post_only = false // aggressive enough to cross spread and fill
        };
        var json = await SendAuthenticatedAsync(HttpMethod.Post, "/v2/orders", order);
        return json.Contains("\"success\":true");
    }

    public async Task<bool> ClosePositionAsync(string symbol)
    {
        try
        {
            var pid = await GetProductIdAsync(symbol);
            var json = await SendAuthenticatedAsync(HttpMethod.Get, $"/v2/positions?product_id={pid}");
            var doc = JsonDocument.Parse(json);
            var size = 0L;
            if (doc.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
                size = result.GetProperty("size").GetInt64();
            if (size == 0) return true;

            var absSize = Math.Abs(size);
            var side = size > 0 ? "sell" : "buy";

            // Use limit order for exit (maker rebate)
            var limitPrice = await GetPriceAsync(symbol);

            var order = new
            {
                product_id = pid,
                size = absSize,
                side,
                order_type = "limit_order",
                price = limitPrice,
                reduce_only = true,
                post_only = false
            };
            var closeJson = await SendAuthenticatedAsync(HttpMethod.Post, "/v2/orders", order);
            return closeJson.Contains("\"success\":true");
        }
        catch
        {
            return false;
        }
    }

    public async Task<decimal> GetBalanceAsync()
    {
        try
        {
            var json = await SendAuthenticatedAsync(HttpMethod.Get, "/v2/wallet/balances");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var result))
            {
                string assets = "";
                foreach (var item in result.EnumerateArray())
                {
                    var asset = item.GetProperty("asset_symbol").GetString() ?? "";
                    decimal balance = 0;
                    if (item.TryGetProperty("balance", out var balProp))
                    {
                        if (balProp.ValueKind == JsonValueKind.String)
                            decimal.TryParse(balProp.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out balance);
                        else if (balProp.ValueKind == JsonValueKind.Number)
                            balance = balProp.GetDecimal();
                    }
                    assets += $"{asset}={balance} ";
                    if (asset is "USD" or "USDT" or "USDC")
                        return balance;
                }
                try { System.IO.File.AppendAllText("C:\\Users\\DEEKSHITH\\logs\\heartbeat.txt", $"Wallet: {assets}{Environment.NewLine}"); } catch { }
            }
            else
            {
                try { System.IO.File.AppendAllText("C:\\Users\\DEEKSHITH\\logs\\heartbeat.txt", $"Wallet no result field: {json}{Environment.NewLine}"); } catch { }
            }
        }
        catch (Exception ex)
        {
            try { System.IO.File.AppendAllText("C:\\Users\\DEEKSHITH\\logs\\heartbeat.txt", $"Wallet error: {ex.Message}{Environment.NewLine}"); } catch { }
        }
        return 0m;
    }

    public async Task<long?> SetStopLossAsync(string symbol, decimal stopPrice, TradeDirection direction, decimal quantity)
    {
        var pid = await GetProductIdAsync(symbol);
        var size = (int)(quantity / 0.01m);
        if (size <= 0) size = 1;
        var side = direction == TradeDirection.Long ? "sell" : "buy";
        var order = new
        {
            product_id = pid,
            size,
            side,
            stop_price = stopPrice.ToString("F2"),
            order_type = "market_order",
            reduce_only = true,
            time_in_force = "gtc"
        };
        try
        {
            var json = await SendAuthenticatedAsync(HttpMethod.Post, "/v2/orders", order);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("id", out var id))
                return id.GetInt64();
            else
                try { System.IO.File.AppendAllText("C:\\Users\\DEEKSHITH\\logs\\heartbeat.txt", $"SL order failed: {json}{Environment.NewLine}"); } catch { }
        }
        catch (Exception ex) { try { System.IO.File.AppendAllText("C:\\Users\\DEEKSHITH\\logs\\heartbeat.txt", $"SL order exception: {ex.Message}{Environment.NewLine}"); } catch { } }
        return null;
    }

    public async Task<long?> SetTakeProfitAsync(string symbol, decimal tpPrice, TradeDirection direction, decimal quantity)
    {
        var pid = await GetProductIdAsync(symbol);
        var size = (int)(quantity / 0.01m);
        if (size <= 0) size = 1;
        var side = direction == TradeDirection.Long ? "sell" : "buy";
        var order = new
        {
            product_id = pid,
            size,
            side,
            limit_price = tpPrice.ToString("F2"),
            order_type = "limit_order",
            reduce_only = true,
            time_in_force = "gtc"
        };
        var json = await SendAuthenticatedAsync(HttpMethod.Post, "/v2/orders", order);
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("id", out var id))
            return id.GetInt64();
        return null;
    }

    public async Task<bool> CancelOrderAsync(long orderId)
    {
        try
        {
            await SendAuthenticatedAsync(HttpMethod.Delete, $"/v2/orders/{orderId}");
            return true;
        }
        catch { return false; }
    }

    public async Task<List<long>> GetOpenOrderIdsAsync(string symbol)
    {
        var ids = new List<long>();
        try
        {
            var pid = await GetProductIdAsync(symbol);
            var json = await SendAuthenticatedAsync(HttpMethod.Get, $"/v2/orders?product_id={pid}&state=open");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array)
            {
                foreach (var order in result.EnumerateArray())
                {
                    if (order.TryGetProperty("id", out var id))
                        ids.Add(id.GetInt64());
                }
            }
        }
        catch { }
        return ids;
    }

    public async Task CancelAllOrdersAsync(string symbol)
    {
        var ids = await GetOpenOrderIdsAsync(symbol);
        foreach (var id in ids)
            await CancelOrderAsync(id);
    }

    public async Task SetLeverageAsync(string symbol, int leverage)
    {
        var pid = await GetProductIdAsync(symbol);
        try
        {
            var json = await SendAuthenticatedAsync(HttpMethod.Post, $"/v2/products/{pid}/orders/leverage", new { leverage });
            try { System.IO.File.AppendAllText("C:\\Users\\DEEKSHITH\\logs\\heartbeat.txt", $"SetLeverage({leverage}x): {json}{Environment.NewLine}"); } catch { }
        }
        catch (Exception ex)
        {
            try { System.IO.File.AppendAllText("C:\\Users\\DEEKSHITH\\logs\\heartbeat.txt", $"SetLeverage failed: {ex.Message}{Environment.NewLine}"); } catch { }
        }
    }

    public async Task SubscribeCandlesAsync(string symbol, string timeframe, Action<Candle> onCandle)
    {
        _onCandle = onCandle;

        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var candles = await GetOHLCVAsync(symbol, timeframe, 2);
                    if (candles.Count > 0)
                    {
                        var latest = candles[^1];
                        _onCandle?.Invoke(latest);
                    }
                }
                catch { }

                var delay = timeframe switch
                {
                    "1m" => 15000, "5m" => 60000, "10m" => 60000,
                    "15m" => 60000, "1h" => 300000, "4h" => 600000,
                    _ => 30000
                };
                await Task.Delay(delay);
            }
        });
    }

    public async Task SubscribeTickerAsync(string symbol, Action<decimal> onPrice)
    {
        _onPrice = onPrice;
        await EnsureWebSocketAsync();

        _tickerSubscriptionMessage = JsonSerializer.Serialize(new
        {
            type = "subscribe",
            payload = new
            {
                channels = new[] { new { name = "v2/ticker", symbols = new[] { symbol } } }
            }
        });
        await SendWsAsync(_tickerSubscriptionMessage);
    }

    private async Task EnsureWebSocketAsync()
    {
        lock (_wsLock)
        {
            if (_ws?.State == WebSocketState.Open)
                return;
        }

        await ConnectWebSocketAsync();

        lock (_wsLock)
        {
            if (_wsReceiveTask == null || _wsReceiveTask.IsCompleted)
                _wsReceiveTask = Task.Run(WebSocketReceiveLoop);
        }
    }

    private async Task ConnectWebSocketAsync()
    {
        lock (_wsLock)
        {
            _ws?.Dispose();
            _wsCts?.Cancel();
            _wsCts = new CancellationTokenSource();
            _ws = new ClientWebSocket();
        }

        await _ws.ConnectAsync(new Uri(_wsUrl), _wsCts!.Token);
    }

    private async Task SendWsAsync(string message)
    {
        ClientWebSocket? ws;
        CancellationToken token;

        lock (_wsLock)
        {
            ws = _ws;
            token = _wsCts?.Token ?? CancellationToken.None;
        }

        if (ws?.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket is not connected");

        var bytes = Encoding.UTF8.GetBytes(message);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, token);
    }

    private async Task WebSocketReceiveLoop()
    {
        var buffer = new byte[16384];
        while (true)
        {
            ClientWebSocket? ws;
            CancellationToken token;

            lock (_wsLock)
            {
                ws = _ws;
                token = _wsCts?.Token ?? CancellationToken.None;
            }

            if (ws?.State != WebSocketState.Open)
            {
                await Task.Delay(5000);
                try { await ConnectWebSocketAsync(); await ResubscribeAsync(); } catch { }
                continue;
            }

            try
            {
                var result = await ws.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await Task.Delay(5000);
                    try { await ConnectWebSocketAsync(); await ResubscribeAsync(); } catch { }
                    continue;
                }

                var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                ProcessWsMessage(msg);
            }
            catch (OperationCanceledException) { break; }
            catch (WebSocketException)
            {
                await Task.Delay(5000);
                try { await ConnectWebSocketAsync(); await ResubscribeAsync(); } catch { }
            }
            catch { }
        }
    }

    private async Task ResubscribeAsync()
    {
        if (!string.IsNullOrEmpty(_tickerSubscriptionMessage))
        {
            try { await SendWsAsync(_tickerSubscriptionMessage); }
            catch { }
        }
    }

    private void ProcessWsMessage(string msg)
    {
        try
        {
            var doc = JsonDocument.Parse(msg);
            if (!doc.RootElement.TryGetProperty("type", out var type)) return;

            var typeStr = type.GetString();

            if ((typeStr == "candle" || typeStr == "candlesticks") && _onCandle != null)
            {
                JsonElement payload;
                if (doc.RootElement.TryGetProperty("payload", out payload))
                {
                    if (payload.ValueKind == JsonValueKind.Array && payload.GetArrayLength() >= 6)
                    {
                        _onCandle(new Candle
                        {
                            Timestamp = DateTimeOffset.FromUnixTimeSeconds(payload[0].GetInt64()).UtcDateTime,
                            Open = payload[1].GetDecimal(),
                            High = payload[2].GetDecimal(),
                            Low = payload[3].GetDecimal(),
                            Close = payload[4].GetDecimal(),
                            Volume = payload[5].GetDecimal()
                        });
                    }
                }
            }
            else if ((typeStr == "v2/ticker" || typeStr == "ticker") && _onPrice != null)
            {
                var payload = doc.RootElement.GetProperty("payload");
                var price = payload.GetProperty("close").GetDecimal();
                _onPrice(price);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        lock (_wsLock)
        {
            _wsCts?.Cancel();
            _ws?.Dispose();
        }
        _http.Dispose();
    }
}
