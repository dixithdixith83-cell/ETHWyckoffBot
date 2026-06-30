using Microsoft.Extensions.DependencyInjection;
using ETHWyckoffBot.Models;

namespace ETHWyckoffBot.Exchange;

public static class ExchangeFactory
{
    public static IExchangeConnector CreateDeltaConnector(AppConfig config)
    {
        return new DeltaConnector(
            config.Delta.BaseUrl,
            config.Delta.ApiKey,
            config.Delta.ApiSecret,
            config.Delta.Symbol
        );
    }

    public static void RegisterExchanges(IServiceCollection services, AppConfig config)
    {
        services.AddSingleton<IExchangeConnector>(sp =>
            CreateDeltaConnector(config));
    }
}
