using ETS2Telemetry.Services;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace ETS2Telemetry.Actions;

internal sealed class StopTelemetryAction(TelemetryPollingService telemetry) : IActionDefinition
{
    public string Id => "stop-telemetry";
    public LocalizedText Name => "Stop telemetry";
    public LocalizedText Description => "Stops polling ETS2/ATS telemetry.";
    public IReadOnlyList<ActionParameter> Parameters { get; } = [];

    public IActionExecutor CreateExecutor() => new Executor(telemetry);

    private sealed class Executor(TelemetryPollingService telemetry) : IActionExecutor
    {
        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
        {
            telemetry.Stop();
            return ActionResult.SucceededTask;
        }
    }
}
