using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace ETS2Telemetry.Services;

/// <summary>
/// Owns the telemetry HTTP polling loop. Replaces the System.Threading.Timer that
/// used to live inside Main.cs / TelemetryPluginInstance under Macro Deck 2.
///
/// Polling starts when the plugin integration is initialized. The
/// StartTelemetryAction/StopTelemetryAction actions can still enable or disable it.
///
/// Field paths below are confirmed against a real telemetry snapshot (2026-08) from
/// the "truck" and "navigation" objects. Each field is parsed independently via
/// TryGetProperty so one shape mismatch only blanks that one value instead of
/// freezing every field declared after it.
///
/// "speed" and "cruiseControlSpeed" are already in km/h on this telemetry server -
/// confirmed empirically (a raw value of ~36 matched real driving speed of 36 km/h),
/// so no unit conversion is applied. "gear" uses displayedGear, which is what the
/// dashboard actually shows (can differ from the raw "gear" field on some
/// transmissions/eco modes).
/// </summary>
public sealed class TelemetryPollingService(
    IHttpClientFactory httpClientFactory,
    ILogger logger) : BackgroundService
{
    private readonly ILogger _logger = logger.ForContext<TelemetryPollingService>();

    private const string TelemetryUrl = "http://localhost:25555/api/ets2/telemetry";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private readonly TaskCompletionSource<bool> _firstSnapshot =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsRunning { get; private set; }
    public bool UseMph { get; set; }

    // Latest snapshot - read by PluginIntegration's IVariableProvider.GetValueAsync.
    public double SpeedKmh { get; private set; }
    public double FuelPercent { get; private set; }
    public int Gear { get; private set; }
    public double SpeedLimitKmh { get; private set; }
    public bool LowBeamOn { get; private set; }
    public bool HighBeamOn { get; private set; }
    public bool ParkingLightsOn { get; private set; }
    public bool LeftBlinkerOn { get; private set; }
    public bool RightBlinkerOn { get; private set; }
    public bool HazardLightsOn { get; private set; }
    public bool ParkingBrakeOn { get; private set; }
    public bool CruiseControlOn { get; private set; }
    public double CruiseControlSpeedKmh { get; private set; }

    public void Start() => IsRunning = true;
    public void Stop() => IsRunning = false;

    public async Task WaitForFirstSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _firstSnapshot.Task.WaitAsync(TimeSpan.FromMilliseconds(750), cancellationToken);
        }
        catch (TimeoutException)
        {
            // The plugin must still start when the telemetry server is unavailable.
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var client = httpClientFactory.CreateClient(nameof(TelemetryPollingService));

        while (!stoppingToken.IsCancellationRequested)
        {
            if (IsRunning)
            {
                try
                {
                    await using var stream = await client.GetStreamAsync(TelemetryUrl, stoppingToken);
                    var doc = await JsonDocument.ParseAsync(stream, cancellationToken: stoppingToken);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("truck", out var truck))
                    {
                        if (TryGetDouble(truck, "speed", out var speed))
                        {
                            SpeedKmh = speed;
                        }

                        if (TryGetDouble(truck, "cruiseControlSpeed", out var ccSpeed))
                        {
                            CruiseControlSpeedKmh = ccSpeed;
                        }

                        if (TryGetBool(truck, "cruiseControlOn", out var ccOn))
                        {
                            CruiseControlOn = ccOn;
                        }

                        if (TryGetDouble(truck, "fuelPercentage", out var fuelPercentage))
                        {
                            FuelPercent = fuelPercentage;
                        }
                        else if (TryGetDouble(truck, "fuel", out var fuelLiters) &&
                                 TryGetDouble(truck, "fuelCapacity", out var fuelCapacity) &&
                                 fuelCapacity > 0)
                        {
                            FuelPercent = fuelLiters / fuelCapacity * 100.0;
                        }

                        if (TryGetInt(truck, "displayedGear", out var gear))
                        {
                            Gear = gear;
                        }

                        if (TryGetBool(truck, "lightsBeamLowOn", out var lowBeam) ||
                            TryGetBool(truck, "lowBeamOn", out lowBeam))
                        {
                            LowBeamOn = lowBeam;
                        }

                        if (TryGetBool(truck, "lightsBeamHighOn", out var highBeam) ||
                            TryGetBool(truck, "highBeamOn", out highBeam))
                        {
                            HighBeamOn = highBeam;
                        }

                        if (TryGetBool(truck, "lightsParkingOn", out var parkingLights) ||
                            TryGetBool(truck, "parkingLightsOn", out parkingLights))
                        {
                            ParkingLightsOn = parkingLights;
                        }

                        // "*On" flickers with the physical bulb (blinking), as opposed to
                        // "*Active" which stays steady while the turn signal is engaged.
                        var haveLeft = TryGetBool(truck, "blinkerLeftOn", out var leftBlinker);
                        if (haveLeft)
                        {
                            LeftBlinkerOn = leftBlinker;
                        }

                        var haveRight = TryGetBool(truck, "blinkerRightOn", out var rightBlinker);
                        if (haveRight)
                        {
                            RightBlinkerOn = rightBlinker;
                        }

                        // ETS2 has no dedicated "hazards" field - hazard lights are just both
                        // blinkers active at once, so derive it from the steady "*Active" state
                        // (not the flickering "*On" state, which would only briefly agree).
                        if (TryGetBool(truck, "blinkerLeftActive", out var leftActive) &&
                            TryGetBool(truck, "blinkerRightActive", out var rightActive))
                        {
                            HazardLightsOn = leftActive && rightActive;
                        }
                        else
                        {
                            HazardLightsOn = LeftBlinkerOn && RightBlinkerOn;
                        }

                        if (TryGetBool(truck, "parkBrakeOn", out var parkBrake) ||
                            TryGetBool(truck, "parkingBrakeOn", out parkBrake))
                        {
                            ParkingBrakeOn = parkBrake;
                        }
                    }

                    if (root.TryGetProperty("navigation", out var navigation) &&
                        (TryGetDouble(navigation, "speedLimit", out var speedLimit) ||
                         TryGetDouble(navigation, "speedLimitKmh", out speedLimit)))
                    {
                        SpeedLimitKmh = speedLimit;
                    }

                    _firstSnapshot.TrySetResult(true);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.Debug(ex, "Telemetry poll failed (server likely not running)");
                }
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static bool TryGetDouble(JsonElement obj, string property, out double value)
    {
        if (!obj.TryGetProperty(property, out var element))
        {
            value = default;
            return false;
        }

        if (element.TryGetDouble(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetInt(JsonElement obj, string property, out int value)
    {
        if (TryGetDouble(obj, property, out var number) &&
            number >= int.MinValue && number <= int.MaxValue)
        {
            value = (int)number;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetBool(JsonElement obj, string property, out bool value)
    {
        if (!obj.TryGetProperty(property, out var element))
        {
            value = default;
            return false;
        }

        if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
        {
            value = element.GetBoolean();
            return true;
        }

        if (TryGetDouble(obj, property, out var number) && (number == 0 || number == 1))
        {
            value = number == 1;
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            bool.TryParse(element.GetString(), out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}
