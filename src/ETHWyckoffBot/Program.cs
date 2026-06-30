using ETHWyckoffBot.Exchange;
using ETHWyckoffBot.Models;
using ETHWyckoffBot.Risk;
using ETHWyckoffBot.Services;
using ETHWyckoffBot.Strategy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace ETHWyckoffBot;

public class Program
{
    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File("logs/trading.log", rollingInterval: RollingInterval.Day, buffered: false)
            .CreateLogger();

        try
        {
            var host = Host.CreateDefaultBuilder(args)
                .UseWindowsService(options =>
                {
                    options.ServiceName = "ETHWyckoffBot";
                })
                .ConfigureServices(ConfigureServices)
                .Build();

            Log.Information("=== ETHWyckoffBot Multi-Coin Service ===");
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error");
            Console.WriteLine($"FATAL: {ex.Message}");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        var config = LoadConfig();
        services.AddSingleton(config);

        var ethClient = new EthOnChainClient(config.OnChain.EthRpcUrl, config.OnChain.MinWhaleValueUsd);
        var btcClient = new BtcBlockchainClient(config.OnChain.BtcApiUrl, config.OnChain.MinWhaleValueUsd);
        var whaleTracker = new WhaleTrackerService(ethClient, btcClient, config.OnChain.MinWhaleValueUsd);
        services.AddSingleton(whaleTracker);

        var riskManager = new RiskManager(config);
        services.AddSingleton(riskManager);

        var notifications = new NotificationService();
        services.AddSingleton(notifications);

        var metrics = new MetricsCollector();
        services.AddSingleton(metrics);

        services.AddSingleton(sp =>
        {
            var connector = ExchangeFactory.CreateDeltaConnector(config);
            var engine = new TradingEngine(connector, config, sp.GetRequiredService<WhaleTrackerService>(),
                riskManager, notifications, metrics);
            engine.SetWhaleTrackerRef(new WhaleTrackerServiceRef(whaleTracker));
            engine.LogMessage += msg => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            engine.BalanceUpdated += bal => Console.WriteLine($"  Balance: {bal:F2} USD");
            return engine;
        });

        var once = Environment.GetCommandLineArgs().Contains("--once");
        services.AddSingleton(new BotMode { Once = once });
        services.AddHostedService<BotService>();
    }

    private static AppConfig LoadConfig()
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch { }
        return new AppConfig();
    }
}

public record BotMode { public bool Once { get; init; } }

public class BotService : BackgroundService
{
    private readonly TradingEngine _engine;
    private readonly BotMode _mode;

    public BotService(TradingEngine engine, BotMode mode)
    {
        _engine = engine;
        _mode = mode;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Information("BotService starting...");
        Console.WriteLine($"Trading {_engine.Symbols.Count} coins. Mode: {(_mode.Once ? "once" : "continuous")}");

        if (_mode.Once)
        {
            await _engine.RunOnceAsync();
        }
        else
        {
            await _engine.StartAsync();

            try
            {
                await Task.Delay(-1, stoppingToken);
            }
            catch (TaskCanceledException) { }

            _engine.Stop();
        }

        Log.Information("BotService stopped");
    }
}
