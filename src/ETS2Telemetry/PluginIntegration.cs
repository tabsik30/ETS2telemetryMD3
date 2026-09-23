using System.Globalization;
using ETS2Telemetry.Actions;
using ETS2Telemetry.ConfigFlow;
using ETS2Telemetry.Services;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace ETS2Telemetry;

/// <summary>
/// The plugin's single integration.
///
/// Rewritten against the Macro Deck 3 beta SDK.
/// IVariableProvider changed completely from preview.3: ProvidedVariables/GetValueAsync
/// are gone, replaced by Variables (IReadOnlyList&lt;VariableDefinition&gt;) and
/// ReadAsync(string localId, CancellationToken) -> ValueTask&lt;VariableReading&gt;.
/// IActionDefinition.Name/Description are now LocalizedText, not string (implicitly
/// convertible from string literals, so the action files barely change).
/// </summary>
internal sealed class PluginIntegration(TelemetryPollingService telemetry)
    : IPluginIntegration, IVariableProvider, IConfigFlowProvider, IWidgetTypeProvider, IUiProvider
{
    private IIntegrationContext? _context;
    private readonly TelemetryWidgetProvider _widgets = new(telemetry);

    public IReadOnlyList<IActionDefinition> Actions { get; } =
    [
        new StartTelemetryAction(telemetry),
        new StopTelemetryAction(telemetry),
    ];

    public async Task InitializeAsync(IIntegrationContext context)
    {
        _context = context;

        // Read back the persisted UseMph setting (written by Ets2ConfigFlow.SubmitAsync via
        // ConfigFlowResult.Complete) so it survives a plugin restart.
        var entries = await context.Config.GetEntriesAsync();
        if (entries.Count > 0)
        {
            var entry = entries[0];
            var useMph = await context.Config.GetStringAsync(entry.Id, "use_mph");
            telemetry.UseMph = useMph == "true";
        }

        telemetry.Start();
        await telemetry.WaitForFirstSnapshotAsync(CancellationToken.None);
    }

    public Task ShutdownAsync()
    {
        telemetry.Stop();
        return Task.CompletedTask;
    }

    // --- IVariableProvider ---

    public IReadOnlyList<VariableDefinition> Variables { get; } =
    [
        VariableDefinition.Eager("speed", VariableType.Text, refreshInterval: TimeSpan.FromMilliseconds(250)),
        VariableDefinition.Eager("fuel-percent", VariableType.Text, refreshInterval: TimeSpan.FromMilliseconds(250)),
        VariableDefinition.Eager("gear", VariableType.Text, refreshInterval: TimeSpan.FromMilliseconds(250)),
        VariableDefinition.Eager("speed-limit", VariableType.Text, refreshInterval: TimeSpan.FromMilliseconds(250)),
        VariableDefinition.Eager("low-beam-on", VariableType.Boolean, refreshInterval: TimeSpan.FromMilliseconds(250)),
        VariableDefinition.Eager("high-beam-on", VariableType.Boolean, refreshInterval: TimeSpan.FromMilliseconds(250)),
        VariableDefinition.Eager("parking-lights-on", VariableType.Boolean, refreshInterval: TimeSpan.FromMilliseconds(250)),
        VariableDefinition.Eager("left-blinker-on", VariableType.Boolean, refreshInterval: TimeSpan.FromMilliseconds(250)),
        VariableDefinition.Eager("right-blinker-on", VariableType.Boolean, refreshInterval: TimeSpan.FromMilliseconds(250)),
        VariableDefinition.Eager("hazard-lights-on", VariableType.Boolean, refreshInterval: TimeSpan.FromMilliseconds(250)),
        VariableDefinition.Eager("parking-brake-on", VariableType.Boolean, refreshInterval: TimeSpan.FromMilliseconds(250)),
        VariableDefinition.Eager("cruise-control-on", VariableType.Boolean, refreshInterval: TimeSpan.FromMilliseconds(250)),
        VariableDefinition.Eager("cruise-control-speed", VariableType.Text, refreshInterval: TimeSpan.FromMilliseconds(250)),
    ];

    public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken)
    {
        object? value = localId switch
        {
            "speed" => FormatWhole(Math.Abs(telemetry.UseMph ? telemetry.SpeedKmh * 0.621371 : telemetry.SpeedKmh)),
            "fuel-percent" => FormatWhole(telemetry.FuelPercent),
            "gear" => FormatGear(telemetry.Gear),
            "speed-limit" => FormatWhole(telemetry.UseMph ? telemetry.SpeedLimitKmh * 0.621371 : telemetry.SpeedLimitKmh),
            "low-beam-on" => telemetry.LowBeamOn,
            "high-beam-on" => telemetry.HighBeamOn,
            "parking-lights-on" => telemetry.ParkingLightsOn,
            "left-blinker-on" => telemetry.LeftBlinkerOn,
            "right-blinker-on" => telemetry.RightBlinkerOn,
            "hazard-lights-on" => telemetry.HazardLightsOn,
            "parking-brake-on" => telemetry.ParkingBrakeOn,
            "cruise-control-on" => telemetry.CruiseControlOn,
            "cruise-control-speed" => FormatWhole(Math.Abs(telemetry.UseMph ? telemetry.CruiseControlSpeedKmh * 0.621371 : telemetry.CruiseControlSpeedKmh)),
            _ => null,
        };

        return ValueTask.FromResult(VariableReading.Of(value));
    }

    // Formats as a plain whole-number string (e.g. "36", not "36.0") - the gauge widgets in
    // the MD3 beta ignored DecimalPlaces and always showed one decimal for Numeric values,
    // so Text sidesteps that host-side formatting entirely.
    private static string FormatWhole(double value) => Math.Round(value).ToString("0", CultureInfo.InvariantCulture);

    // ETS2's displayedGear is negative for reverse gears (e.g. -1 for R1) and 0 for neutral.
    // Show "R1"/"R2" and "N" instead of raw negative numbers / 0.
    private static string FormatGear(int gear) => gear switch
    {
        0 => "N",
        > 0 => gear.ToString(CultureInfo.InvariantCulture),
        < 0 => $"R{-gear}",
    };

    // --- IConfigFlowProvider ---

    public IConfigFlow CreateConfigFlow() => new Ets2ConfigFlow(telemetry);

    // --- Widget provider ---

    public string ProviderName => _widgets.ProviderName;

    public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() => _widgets.GetWidgetTypes();

    public Task InitializeAsync(
        IWidgetTypeProviderContext context,
        CancellationToken cancellationToken = default) =>
        _widgets.InitializeAsync(context, cancellationToken);

    // --- UI provider ---

    public IReadOnlyList<UiSurfaceDeclaration> Surfaces => _widgets.Surfaces;

    public Task<IUiSession?> CreateSessionAsync(
        UiSessionRequest request,
        CancellationToken cancellationToken) =>
        _widgets.CreateSessionAsync(request, cancellationToken);
}
