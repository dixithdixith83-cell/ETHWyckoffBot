using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Exchange;

public interface IExchangeConnector
{
    string ExchangeName { get; }
    Task<string> TestConnectionAsync();
    Task<List<Candle>> GetOHLCVAsync(string symbol, string timeframe, int limit);
    Task<decimal> GetPriceAsync(string symbol);
    Task<Position?> GetPositionAsync(string symbol);
    Task<bool> PlaceOrderAsync(string symbol, TradeDirection direction, decimal quantity, decimal price);
    Task<bool> ClosePositionAsync(string symbol);
    Task<long?> SetStopLossAsync(string symbol, decimal stopPrice, TradeDirection direction, decimal quantity);
    Task<long?> SetTakeProfitAsync(string symbol, decimal tpPrice, TradeDirection direction, decimal quantity);
    Task<bool> CancelOrderAsync(long orderId);
    Task<List<long>> GetOpenOrderIdsAsync(string symbol);
    Task CancelAllOrdersAsync(string symbol);
    Task<decimal> GetBalanceAsync();
    Task SetLeverageAsync(string symbol, int leverage);
    Task SubscribeCandlesAsync(string symbol, string timeframe, Action<Candle> onCandle);
    Task SubscribeTickerAsync(string symbol, Action<decimal> onPrice);
}
