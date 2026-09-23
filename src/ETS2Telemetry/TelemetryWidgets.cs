using System.Globalization;
using System.Text.Json;
using ETS2Telemetry.Services;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;

namespace ETS2Telemetry;

internal sealed class TelemetryWidgetProvider(TelemetryPollingService telemetry)
    : IWidgetTypeProvider, IUiProvider
{
    public const string GaugeType = "gauge";
    private const string GaugeQualifiedType = "com.tabsik12.ets2-telemetry::gauge";

    private static readonly string DataSchema = """
        {
          "type": "object",
          "properties": {
            "label": { "type": "string" },
            "maxSpeed": { "type": "number", "minimum": 1, "maximum": 500 },
            "unit": { "type": "string", "enum": ["auto", "kmh", "mph"] },
            "background": { "type": "string" },
            "labelOffsetX": { "type": "number", "minimum": -1, "maximum": 1 },
            "labelOffsetY": { "type": "number", "minimum": -1, "maximum": 1 },
            "mode": { "type": "string", "enum": ["speed", "fuel"] }
            ,"scale": { "type": "string", "enum": ["auto", "74", "100", "120"] }
          },
          "additionalProperties": true
        }
        """;

    public string ProviderName => "ETS2 Telemetry";

    public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
    [
        new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
        new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
        new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
    ];

    public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() =>
    [
        new WidgetTypeDescriptor(
            GaugeType,
            "ETS2 Gauge",
            "Speedometer or fuel gauge with a locally rotated needle",
            "{\"label\":\"km/h\",\"maxSpeed\":120,\"unit\":\"auto\",\"mode\":\"speed\",\"scale\":\"auto\",\"background\":\"\",\"labelOffsetX\":0,\"labelOffsetY\":0.45}",
            DataSchema,
            HasConfiguration: true),
    ];

    public async Task InitializeAsync(
        IWidgetTypeProviderContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var widgetType in GetWidgetTypes())
        {
            await context.RegisterWidgetTypeAsync(widgetType, cancellationToken);
        }
    }

    public Task<IUiSession?> CreateSessionAsync(
        UiSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Surface.Kind is not UiSurfaceKinds.Widget and not UiSurfaceKinds.Preview)
        {
            if (request.Surface.Kind == UiSurfaceKinds.Config)
            {
                if (request.Surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.WidgetType, out var configType) &&
                    configType.GetString() == GaugeQualifiedType)
                {
                    return Task.FromResult<IUiSession?>(new TelemetryGaugeSession.TelemetryWidgetConfigurationSession(request.Surface));
                }

                return Task.FromResult<IUiSession?>(null);
            }
            return Task.FromResult<IUiSession?>(null);
        }

        if (!request.Surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.WidgetType, out var typeElement))
        {
            return Task.FromResult<IUiSession?>(null);
        }

        var widgetType = typeElement.GetString();
        if (widgetType != GaugeQualifiedType)
        {
            return Task.FromResult<IUiSession?>(null);
        }

        var data = ReadData(request.Surface);
        IUiSession session = new TelemetryGaugeSession(request.Surface, telemetry, data);
        return Task.FromResult<IUiSession?>(session);
    }

    private static WidgetData ReadData(UiSurface surface)
    {
        if (!surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Data, out var data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            return new WidgetData("", 120, "auto", "speed", "auto", "", 0, 0.45);
        }

        var label = data.TryGetProperty("label", out var labelElement)
            ? labelElement.GetString() ?? ""
            : "";
        var maxSpeed = data.TryGetProperty("maxSpeed", out var maxElement) &&
                       maxElement.TryGetDouble(out var configuredMax)
            ? Math.Clamp(configuredMax, 1, 500)
            : 120;
        var unit = data.TryGetProperty("unit", out var unitElement)
            ? unitElement.GetString() ?? "auto"
            : "auto";
        var background = data.TryGetProperty("background", out var backgroundElement)
            ? backgroundElement.GetString() ?? ""
            : "";
        var labelOffsetY = data.TryGetProperty("labelOffsetY", out var offsetElement) &&
                           offsetElement.TryGetDouble(out var configuredOffset)
            ? Math.Clamp(configuredOffset, -1, 1)
            : -0.25;
        var labelOffsetX = data.TryGetProperty("labelOffsetX", out var offsetXElement) &&
                           offsetXElement.TryGetDouble(out var configuredOffsetX)
            ? Math.Clamp(configuredOffsetX, -1, 1)
            : 0;
        var mode = data.TryGetProperty("mode", out var modeElement)
            ? modeElement.GetString() ?? "speed"
            : "speed";
        var scale = data.TryGetProperty("scale", out var scaleElement)
            ? scaleElement.GetString() ?? "auto"
            : "auto";
        return new WidgetData(label, maxSpeed, unit, mode == "fuel" ? "fuel" : "speed", scale, background, labelOffsetX, labelOffsetY);
    }

    private sealed record WidgetData(
        string Label,
        double MaxSpeed,
        string Unit,
        string Mode,
        string Scale,
        string Background,
        double LabelOffsetX,
        double LabelOffsetY);

    private sealed class TelemetryGaugeSession : IUiSession
    {
        private readonly UiSurface _surface;
        private readonly TelemetryPollingService _telemetry;
        private readonly WidgetData _data;
        private readonly CancellationTokenSource _stop = new();
        private readonly object _sync = new();
        private readonly List<UiPatch> _patches = [];
        private UiTree _tree;
        private int _revision;
        private double _lastValue = double.NaN;
        private bool _disposed;

        public TelemetryGaugeSession(
            UiSurface surface,
            TelemetryPollingService telemetry,
            WidgetData data)
        {
            _surface = surface;
            _telemetry = telemetry;
            _data = data;
            _tree = new UiTree
            {
                Revision = 0,
                Surface = surface,
                Root = BuildRoot(0),
            };
            _ = UpdateLoopAsync();
        }

        internal sealed class TelemetryWidgetConfigurationSession : IUiSession
        {
            private readonly UiView _view;

            public TelemetryWidgetConfigurationSession(UiSurface surface)
            {
                var data = ReadConfigurationData(surface);
                var label = new UiState<string>(data.Label);
                var maxSpeed = new UiState<double>(data.MaxSpeed);
                var unit = new UiState<string>(data.Unit);
                var mode = new UiState<string>(data.Mode);
                var scale = new UiState<string>(data.Scale);
                var background = new UiState<string>(data.Background);
                var labelOffsetX = new UiState<double>(data.LabelOffsetX);
                var labelOffsetY = new UiState<double>(data.LabelOffsetY);

                var root = new UiWidgetConfiguration
                {
                    Key = "root",
                    Properties = new UiWidgetProperties
                    {
                        Key = "properties",
                        Children =
                        [
                            new UiStringInput
                            {
                                Key = "mode",
                                Label = "Display: speed or fuel",
                                Binding = Bind.To(mode),
                            },
                            new UiStringInput
                            {
                                Key = "scale",
                                Label = "Scale: auto, 74, 100 or 120",
                                Binding = Bind.To(scale),
                            },
                            new UiStringInput
                            {
                                Key = "label",
                                Label = "Label",
                                Binding = Bind.To(label),
                            },
                            new UiNumberInput
                            {
                                Key = "maxSpeed",
                                Label = "Maximum speed (custom scale)",
                                Binding = Bind.To(maxSpeed),
                                Min = 1,
                                Max = 500,
                                Step = 1,
                                ShowSlider = true,
                            },
                            new UiStringInput
                            {
                                Key = "unit",
                                Label = "Unit: auto, kmh or mph",
                                Binding = Bind.To(unit),
                            },
                            new UiImageInput
                            {
                                Key = "background",
                                Label = "Background image",
                                Binding = Bind.To(background),
                                FileExtensions = new[] { "png", "jpg", "jpeg", "webp" },
                            },
                            new UiNumberInput
                            {
                                Key = "labelOffsetX",
                                Label = "Label horizontal position",
                                Binding = Bind.To(labelOffsetX),
                                Min = -1,
                                Max = 1,
                                Step = 0.01,
                                ShowSlider = true,
                            },
                            new UiNumberInput
                            {
                                Key = "labelOffsetY",
                                Label = "Label vertical position",
                                Binding = Bind.To(labelOffsetY),
                                Min = -1,
                                Max = 1,
                                Step = 0.01,
                                ShowSlider = true,
                            },
                        ],
                    },
                };
                _view = new UiView(surface, root);
            }

            public event EventHandler? Changed
            {
                add => _view.Changed += value;
                remove => _view.Changed -= value;
            }

            public event EventHandler<UiSessionFaultedEventArgs>? Faulted
            {
                add { }
                remove { }
            }

            public UiTree BuildTree() => _view.Tree;

            public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

            public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            private static WidgetData ReadConfigurationData(UiSurface surface)
            {
                if (surface.Attributes.TryGetValue("widgetData", out var data) &&
                    data.ValueKind == JsonValueKind.Object)
                {
                    return ReadDataObject(data);
                }
                return new WidgetData("", 120, "auto", "speed", "auto", "", 0, 0.45);
            }
        }

        private static WidgetData ReadDataObject(JsonElement data)
        {
            var label = data.TryGetProperty("label", out var labelElement)
                ? labelElement.GetString() ?? ""
                : "";
            var maxSpeed = data.TryGetProperty("maxSpeed", out var maxElement) &&
                           maxElement.TryGetDouble(out var configuredMax)
                ? Math.Clamp(configuredMax, 1, 500)
                : 120;
            var unit = data.TryGetProperty("unit", out var unitElement)
                ? unitElement.GetString() ?? "auto"
                : "auto";
            var background = data.TryGetProperty("background", out var backgroundElement)
                ? backgroundElement.GetString() ?? ""
                : "";
            var labelOffsetY = data.TryGetProperty("labelOffsetY", out var offsetElement) &&
                               offsetElement.TryGetDouble(out var configuredOffset)
                ? Math.Clamp(configuredOffset, -1, 1)
                : -0.25;
            var labelOffsetX = data.TryGetProperty("labelOffsetX", out var offsetXElement) &&
                               offsetXElement.TryGetDouble(out var configuredOffsetX)
                ? Math.Clamp(configuredOffsetX, -1, 1)
                : 0;
            var mode = data.TryGetProperty("mode", out var modeElement)
                ? modeElement.GetString() ?? "speed"
                : "speed";
            var scale = data.TryGetProperty("scale", out var scaleElement)
                ? scaleElement.GetString() ?? "auto"
                : "auto";
            return new WidgetData(label, maxSpeed, unit, mode == "fuel" ? "fuel" : "speed", scale, background, labelOffsetX, labelOffsetY);
        }

        public event EventHandler? Changed;
        public event EventHandler<UiSessionFaultedEventArgs>? Faulted
        {
            add { }
            remove { }
        }

        public UiTree BuildTree()
        {
            lock (_sync)
            {
                return _tree;
            }
        }

        public IReadOnlyList<UiPatch> DrainPatches()
        {
            lock (_sync)
            {
                var patches = _patches.ToArray();
                _patches.Clear();
                return patches;
            }
        }

        public void Dispatch(UiEvent uiEvent)
        {
        }

        public ValueTask DisposeAsync()
        {
            lock (_sync)
            {
                _disposed = true;
            }
            _stop.Cancel();
            _stop.Dispose();
            return ValueTask.CompletedTask;
        }

        private async Task UpdateLoopAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var value = _data.Mode == "fuel"
                        ? Math.Clamp(_telemetry.FuelPercent, 0, 100)
                        : DisplaySpeed();
                    if (double.IsNaN(_lastValue) || Math.Abs(value - _lastValue) >= 0.1)
                    {
                        Update(value);
                    }
                    await Task.Delay(50, _stop.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void Update(double value)
        {
            UiPatch patch;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _lastValue = value;
                _revision++;
                var properties = new Dictionary<string, JsonElement>
                {
                    ["rotation"] = JsonSerializer.SerializeToElement(ToNeedleAngle(value)),
                    ["text"] = JsonSerializer.SerializeToElement(FormatValue(value)),
                };
                var valueNodeId = "value-text";
                var operations = new List<UiPatchOperation>
                {
                    new()
                    {
                        Op = UiPatchOperations.SetProperties,
                        NodeId = "needle",
                        Properties = new Dictionary<string, JsonElement> { ["rotation"] = properties["rotation"] },
                    },
                    new()
                    {
                        Op = UiPatchOperations.SetProperties,
                        NodeId = valueNodeId,
                        Properties = new Dictionary<string, JsonElement> { ["text"] = properties["text"] },
                    },
                };
                patch = new UiPatch { FromRevision = _revision - 1, ToRevision = _revision, Operations = operations };
                _patches.Add(patch);
                _tree = new UiTree { Revision = _revision, Surface = _surface, Root = _tree.Root };
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private UiNode BuildRoot(double value)
        {
            return BuildDialRoot(value);
        }

        private UiNode BuildDialRoot(double value)
        {
            var children = new List<UiNode>();
            if (!string.IsNullOrWhiteSpace(_data.Background))
            {
                children.Add(new UiNode
                {
                    Id = "background",
                    Type = "ui.image",
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["source"] = JsonSerializer.SerializeToElement(new UiResource
                        {
                            ResourceId = _data.Background,
                        }),
                        ["size"] = JsonSerializer.SerializeToElement(new { basis = 1.0 }),
                    },
                });
            }

            children.AddRange(
            [
                ..BuildScaleNodes(),
                new UiNode
                {
                    Id = "needle",
                    Type = "ui.transform",
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["fill"] = JsonSerializer.SerializeToElement(true),
                        ["rotation"] = JsonSerializer.SerializeToElement(ToNeedleAngle(value)),
                        ["originX"] = JsonSerializer.SerializeToElement(0.5),
                        ["originY"] = JsonSerializer.SerializeToElement(0.5),
                        ["zoom"] = JsonSerializer.SerializeToElement(0.72),
                    },
                    Children =
                    [
                        new UiNode
                        {
                            Id = "needle-line",
                            Type = "ui.text",
                            Properties = TextProperties("|", 0.55, "#e85d4a", "center"),
                        },
                    ],
                },
                new UiNode
                {
                    Id = "dial-label",
                    Type = "ui.transform",
                    Properties = TransformProperties(_data.LabelOffsetX, _data.LabelOffsetY),
                    Children =
                    [
                        new UiNode
                        {
                            Id = "label-text",
                            Type = "ui.text",
                            Properties = TextProperties(_data.Label, 0.12, "#aeb9c7", "center"),
                        },
                    ],
                },
                new UiNode
                {
                    Id = "value",
                    Type = "ui.stack",
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["fill"] = JsonSerializer.SerializeToElement(true),
                        ["direction"] = JsonSerializer.SerializeToElement("vertical"),
                        ["justify"] = JsonSerializer.SerializeToElement("end"),
                        ["align"] = JsonSerializer.SerializeToElement("center"),
                        ["padding"] = JsonSerializer.SerializeToElement(new { basis = 0.08 }),
                    },
                    Children =
                    [
                        new UiNode
                        {
                            Id = "value-text",
                            Type = "ui.text",
                            Properties = TextProperties(FormatValue(value) + (_data.Mode == "fuel" ? "%" : ""), 0.25, "#ffffff", "center"),
                        },
                    ],
                },
            ]);

            return new UiNode
            {
                Id = "root",
                Type = "ui.layer",
                Children = children,
            };
        }

        private static Dictionary<string, JsonElement> TransformProperties(
            double offsetX,
            double offsetY,
            double rotation = 0) =>
            new()
            {
                ["fill"] = JsonSerializer.SerializeToElement(true),
                ["originX"] = JsonSerializer.SerializeToElement(0.5),
                ["originY"] = JsonSerializer.SerializeToElement(0.5),
                ["rotation"] = JsonSerializer.SerializeToElement(rotation),
                ["offsetX"] = JsonSerializer.SerializeToElement(offsetX),
                ["offsetY"] = JsonSerializer.SerializeToElement(offsetY),
            };

        private static string FormatValue(double value) => Math.Round(value).ToString("0", CultureInfo.InvariantCulture);

        private double ToNeedleAngle(double speed)
        {
            var scale = DisplayScale();
            var normalized = Math.Clamp(speed / scale, 0, 1);
            return -120 + normalized * 240;
        }

        private double DisplayScale()
        {
            if (double.TryParse(_data.Scale, NumberStyles.Integer, CultureInfo.InvariantCulture, out var manualScale))
            {
                return manualScale;
            }

            if (_data.Mode == "fuel")
            {
                return 100;
            }

            return _data.Unit == "mph" || (_data.Unit == "auto" && _telemetry.UseMph) ? 74 : 120;
        }

        private List<UiNode> BuildScaleNodes()
        {
            var values = ScaleValues();
            var nodes = new List<UiNode>();
            for (var index = 0; index < values.Length; index++)
            {
                var angle = -120 + index * 240.0 / (values.Length - 1);
                nodes.Add(new UiNode
                {
                    Id = $"scale-{index}",
                    Type = "ui.transform",
                    Properties = TransformProperties(0, 0, angle),
                    Children =
                    [
                        new UiNode
                        {
                            Id = $"scale-{index}-position",
                            Type = "ui.stack",
                            Properties = new Dictionary<string, JsonElement>
                            {
                                ["fill"] = JsonSerializer.SerializeToElement(true),
                                ["direction"] = JsonSerializer.SerializeToElement("vertical"),
                                ["justify"] = JsonSerializer.SerializeToElement("start"),
                                ["align"] = JsonSerializer.SerializeToElement("center"),
                                ["padding"] = JsonSerializer.SerializeToElement(new { basis = 0.0 }),
                            },
                            Children =
                            [
                                new UiNode
                                {
                                    Id = $"scale-{index}-text",
                                    Type = "ui.text",
                                    Properties = TextProperties(
                                        values[index].ToString("0", CultureInfo.InvariantCulture),
                                        0.075,
                                        "#aeb9c7",
                                        "center"),
                                },
                            ],
                        },
                    ],
                });
            }

            return nodes;
        }

        private double[] ScaleValues()
        {
            var max = DisplayScale();
            if (_data.Mode == "fuel")
            {
                return [0, 20, 40, 60, 80, 100];
            }

            if (Math.Abs(max - 120) < 0.1)
            {
                return [0, 20, 40, 60, 80, 100, 120];
            }

            if (Math.Abs(max - 74) < 0.1)
            {
                return [0, 12, 25, 37, 49, 62, 74];
            }

            return Enumerable.Range(0, 7)
                .Select(index => max * index / 6)
                .ToArray();
        }

        private double DisplaySpeed()
        {
            var useMph = _data.Unit switch
            {
                "mph" => true,
                "kmh" => false,
                _ => _telemetry.UseMph,
            };
            return Math.Abs(useMph ? _telemetry.SpeedKmh * 0.621371 : _telemetry.SpeedKmh);
        }

        private static Dictionary<string, JsonElement> TextProperties(string text, double size, string color, string align) =>
            new()
            {
                ["text"] = JsonSerializer.SerializeToElement(text),
                ["size"] = JsonSerializer.SerializeToElement(new { basis = size }),
                ["color"] = JsonSerializer.SerializeToElement(color),
                ["align"] = JsonSerializer.SerializeToElement(align),
            };
    }
}
