using ETS2Telemetry.Services;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace ETS2Telemetry.Actions;

internal sealed class StartTelemetryAction(TelemetryPollingService telemetry) : IActionDefinition
{
    public string Id => "start-telemetry";
    public LocalizedText Name => "Start telemetry";
    public LocalizedText Description => "Starts polling ETS2/ATS telemetry.";
    public IReadOnlyList<ActionParameter> Parameters { get; } = [];

    public IActionExecutor CreateExecutor() => new Executor(telemetry);

    private sealed class Executor(TelemetryPollingService telemetry) : IActionExecutor
    {
        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
        {
            telemetry.Start();
            return ActionResult.SucceededTask;
        }
    }
}
