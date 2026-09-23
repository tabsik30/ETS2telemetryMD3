using ETS2Telemetry.Services;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ETS2Telemetry;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = MacroDeckPlugin.CreatePlugin(args)
            .UseMacroDeckLogging()
            .RegisterIntegration<PluginIntegration>();

        // TelemetryPollingService is a BackgroundService: it owns the HTTP polling
        // loop that used to live in Main.cs's Timer under Macro Deck 2. It is
        // registered as a singleton so PluginIntegration (variables) and the
        // start/stop actions can all reach the same running instance via DI.
        builder.Services.AddSingleton<TelemetryPollingService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<TelemetryPollingService>());
        builder.Services.AddHttpClient();

        var plugin = builder.Build();
        await plugin.RunAsync();
    }
}
