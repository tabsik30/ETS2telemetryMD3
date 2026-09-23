using ETS2Telemetry.Services;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;

namespace ETS2Telemetry.ConfigFlow;

/// <summary>
/// One-step config flow replacing the WinForms SettingsForm from Macro Deck 2.
/// The host renders the single "settings" step's Fields itself - there is no plugin-side UI code.
/// </summary>
internal sealed class Ets2ConfigFlow(TelemetryPollingService telemetry) : IConfigFlow
{
    private const string StepId = "settings";
    private const string UseMphFieldName = "use_mph";

    public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
    {
        var step = new ConfigFlowStep
        {
            StepId = StepId,
            Title = "ETS2/ATS Telemetry settings",
            Fields =
            [
                ActionParameter.Toggle(
                    UseMphFieldName,
                    label: "Use mph",
                    description: "Show speed in mph instead of km/h.",
                    defaultValue: telemetry.UseMph),
            ],
        };

        return Task.FromResult(ConfigFlowResult.Step(step));
    }

    public Task<ConfigFlowResult> SubmitAsync(
        string stepId,
        IReadOnlyDictionary<string, object?> input,
        IConfigFlowContext context,
        CancellationToken cancellationToken)
    {
        var useMph = input.TryGetValue(UseMphFieldName, out var raw) && raw is bool b && b;
        telemetry.UseMph = useMph;

        var values = new Dictionary<string, ConfigFlowValue>
        {
            [UseMphFieldName] = ConfigFlowValue.Plain(useMph ? "true" : "false"),
        };

        return Task.FromResult(ConfigFlowResult.Complete("ETS2/ATS Telemetry", values));
    }
}
