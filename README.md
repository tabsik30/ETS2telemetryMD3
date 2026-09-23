# Macro Deck plugin template

A starting point for an out-of-process Macro Deck 3 plugin: one integration that starts, registers with
a host and does nothing else. No sample capabilities to delete, no example code to read around - add
what your plugin actually needs.

Looking for worked examples of each capability instead? The
[sample plugins repository](https://github.com/Macro-Deck-App/Macro-Deck-Sample-Plugins) has one
coherent plugin per area: actions and variables, a music player, a REST API with a multi-step config
flow, a virtual profile.

## Getting started

Either install the template and generate a project:

```bash
dotnet new install MacroDeck.Plugin.Templates@*-*
```

`@*-*` installs the newest published version. The floating form is what you want while the 3.0
template is in preview: `dotnet new install` picks stable versions by default, and there is no stable
release yet.

```bash
dotnet new macrodeck-plugin -n Acme.LightControl --pluginId com.acme.light-control --pluginName "Acme Light Control"
```

| Parameter | Default | What it sets |
| --- | --- | --- |
| `-n`, `--name` | `MacroDeckPlugin` | The project, namespace, solution and the executable names in `manifest.json` |
| `--pluginId` | `com.example.my-plugin` | The manifest `id`: reverse-domain, lowercase, at least two dot-joined kebab segments |
| `--pluginName` | `My Plugin` | The display name Macro Deck shows |

Or clone this repository and rename by hand - the two are the same content. If you clone, change the
`id`, `name`, `version` and `description` in `src/ETS2Telemetry/manifest.json`, then rename
the projects, the solution file and the namespace.

Either way, replace `Assets/icon.svg`. It is your plugin's icon: the manifest's `icon` path is the
single source of truth and the host reads that file directly, so there is no code to change.

## Requirements

- .NET SDK 10.0
- A running Macro Deck desktop app for [interactive debugging](#run-and-debug-against-macro-deck)

## Quick start

```bash
dotnet build
```

```bash
dotnet test
```

Build and tests need no Macro Deck installation. For an interactive session, use the checked-in
**Macro Deck - Real Host** launch profile after the one-time setup below.

## Building against a local SDK build

The template tracks the SDK's *published* packages and floats to the newest one, so a plain
`dotnet build` always resolves the latest release. While a change is still unreleased, pack the SDK
from a Macro Deck 3 checkout into this repository's `local-feed/` and build against that version:

```bash
dotnet pack MacroDeck.slnx -c Release -p:Version=3.0.0-local.1 -o <path-to-this-repo>/local-feed
```

```bash
dotnet build -p:MacroDeckSdkVersion=3.0.0-local.1
```

`NuGet.config` already lists `local-feed/` as a package source, and `MacroDeckSdkVersion` sets the
version for every Macro Deck package at once (see `Directory.Packages.props`). Nothing in the
repository pins the local version, so a plain `dotnet build` goes back to the published one.

Pick a version that cannot collide with a real release - `3.0.0-local.N` rather than reusing a
published preview version, which would put a hand-built package into the global NuGet cache under the
name of a published one.

## How a plugin is put together

### Project layout

```
src/ETS2Telemetry/
  Program.cs             the host builder - three lines and a RunAsync
  manifest.json          identity, icon and per-platform entrypoints
  PluginIntegration.cs   the integration: lifecycle and capability opt-ins
  Assets/icon.svg        the icon the manifest declares
  Properties/launchSettings.json   the shared real-host debug profile
tests/ETS2Telemetry.Tests/
  PluginIntegrationTests.cs   the plugin builds and initializes
```

### The entry point

`MacroDeckPlugin.CreatePlugin(args)` wraps `WebApplication.CreateBuilder`, so everything an ASP.NET
Core application has is available - configuration, options binding, `IHttpClientFactory`, hosted
services, dependency injection:

```csharp
var plugin = MacroDeckPlugin.CreatePlugin(args)
    .UseMacroDeckLogging()
    .RegisterIntegration<PluginIntegration>()
    .Build();

await plugin.RunAsync();
```

`RegisterIntegration<T>()` is the one door: it registers the integration's actions plus a capability
handler for every SDK interface the type implements. The integration is built by DI, so it can take
`IHttpClientFactory`, `IOptions<T>`, Serilog's `ILogger`, `PluginMetadata` or `IPluginCatalogNotifier`
in its constructor. `UseMacroDeckLogging()` routes your log output to the host's log viewer.

Anything the container needs beyond that goes on `builder.Services` before `Build()`.

### The manifest

`manifest.json` is the plugin's identity, read from the content root at startup. `Build()` validates it
and fails fast on an invalid id, a missing name or version, or an unreadable icon.

```json
{
  "manifestVersion": 1,
  "id": "com.tabsik12.ets2-telemetry",
  "name": "ETS2/ATS Telemetry",
  "version": "1.0.0",
  "description": "A minimal Macro Deck 3 plugin.",
  "icon": "Assets/icon.svg",
  "entrypoints": {
    "win-x64": { "executable": "ETS2Telemetry.exe" },
    "osx-arm64": { "executable": "ETS2Telemetry" },
    "osx-x64": { "executable": "ETS2Telemetry" },
    "linux-x64": { "executable": "ETS2Telemetry" }
  }
}
```

Only `manifestVersion`, `id`, `name`, `version` and `entrypoints` are required. A manifest may also
declare `permissions`, `dependencies`, `conflicts`, `iconPacks`, `compatibility` and `files[]` -
`macrodeck-plugin inspect` reports all of them, and `pack` recomputes `files[]` for you.

`win-arm64` falls back to `win-x64` and `osx-arm64` falls back to `osx-x64`; there is no `"any"` key,
and `linux-musl-*` resolves no fallback at all.

### Capabilities

`PluginIntegration` implements `IPluginIntegration` - lifecycle and actions - and **opts into**
everything else by implementing that capability's interface. The host discovers each one by filtering
on the interface, so you only implement what you need. Identity and the icon are not on this list:
they come from the manifest.

| Capability | Interface |
| --- | --- |
| Actions | `IActionDefinition`, in `Actions` |
| Config flow | `IConfigFlowProvider` |
| Variables | `IVariableProvider` |
| Events | `IEventProvider` |
| Issues | `IIntegrationIssueProvider` |
| Music players | `IMusicPlayerProvider` |
| Weather | `IWeatherProvider` |
| Virtual profiles | `IProfileProvider` |

An integration that provides a config flow starts **disabled** until the user configures it; everything
else defaults to enabled.

Capability ids are namespaced by the host as `integrationId::localId`, so you declare provider-local
ids and never the qualified form.

The [sample plugins](https://github.com/Macro-Deck-App/Macro-Deck-Sample-Plugins) are the worked
examples for each of these.

## Run and debug against Macro Deck

The project contains exactly one interactive launch profile: **Macro Deck - Real Host**. It launches
the plugin project directly, so Rider and Visual Studio attach the debugger to plugin code without a
wrapper or child-process attach. The profile connects in self-registering mode to the installed Macro
Deck desktop app at `http://127.0.0.1:8193`.

For the first run:

1. Start Macro Deck.
2. Open **Developer Tools → Plugin tokens**, create a token and copy it. It is shown only once.
3. Store the token in the source project's **.NET User Secrets** using one of the methods below. The
   project is already initialized; do not run `dotnet user-secrets init`.
4. Select **Macro Deck - Real Host** and start it with **Debug**.
5. Once enrollment succeeds, remove the token from User Secrets.

### Set the token in Rider or Visual Studio

In Rider, right-click `ETS2Telemetry` in the Solution Explorer and select
**Tools → .NET User Secrets**. In Visual Studio, right-click the same source project and select
**Manage User Secrets**. Do not select the `.Tests` project.

The IDE opens a `secrets.json` file stored in your user profile, outside this repository. Replace its
contents with:

```json
{
  "MacroDeck:Plugin:EnrollmentToken": "<paste the one-time token here>"
}
```

Save the file, then start **Macro Deck - Real Host**. After enrollment, reopen `secrets.json` and
remove the `MacroDeck:Plugin:EnrollmentToken` entry.

### Set the token from a terminal

From the repository root on macOS or Linux, use the following form. It reads the token without echoing
it and does not put the value in shell history or process arguments:

```bash
project="src/ETS2Telemetry/ETS2Telemetry.csproj"
printf "Enrollment token: "
read -rs md_enrollment_token
printf '\n'
printf '{"MacroDeck:Plugin:EnrollmentToken":"%s"}\n' "$md_enrollment_token" |
  dotnet user-secrets set --project "$project"
unset md_enrollment_token
```

After the first successful profile launch, remove the one-time token:

```bash
dotnet user-secrets remove "MacroDeck:Plugin:EnrollmentToken" --project "$project"
```

With PowerShell 7, use the equivalent masked-input form:

```powershell
$project = "src/ETS2Telemetry/ETS2Telemetry.csproj"
$token = Read-Host "Enrollment token" -MaskInput
@{ "MacroDeck:Plugin:EnrollmentToken" = $token } |
  ConvertTo-Json -Compress |
  dotnet user-secrets set --project $project
Remove-Variable token
```

Then remove it after enrollment:

```powershell
dotnet user-secrets remove "MacroDeck:Plugin:EnrollmentToken" --project $project
```

The profile persists the exchanged plugin credential under
`src/ETS2Telemetry/.macrodeck-dev-state/`, which is ignored by Git and excluded from the
template package. Later profile launches reuse that credential. The User Secrets id is renamed with a
generated project, so each plugin gets a separate local secret store.

User Secrets are local-only but not encrypted. Never put the enrollment token in `launchSettings.json`,
a shared IDE configuration, a literal command argument or a commit. If you intentionally clear the
local state, create a fresh token and repeat the User Secrets step. Self-registration only works
against a host on the same machine. See the official
[Rider User Secrets guide](https://www.jetbrains.com/help/rider/Manage_NET_user_secrets.html) and
[.NET Secret Manager guide](https://learn.microsoft.com/aspnet/core/security/app-secrets?view=aspnetcore-10.0)
for more background.

## The developer CLI

`macrodeck-plugin` validates, inspects, packs and conformance-tests a plugin. Interactive starts use the
launch profile above.

```bash
dotnet tool install --global MacroDeck.Plugin.Cli --prerelease
```

`--prerelease` is required while the 3.0 SDK is in preview: only preview versions are published, and
`dotnet tool install` picks stable ones by default. Drop it once 3.0 ships.

The tool needs the **ASP.NET Core shared framework**, not just the .NET runtime - its stub host is a
real Kestrel server.

| Command | What it does |
| --- | --- |
| `validate` | Checks a manifest, version directory or artifact against the real manifest reader, the JSON Schema, the permission vocabulary and declared file digests. |
| `inspect` | Reports what installing an artifact would find - entrypoints, permissions, dependencies, conflicts, compatibility, signature shape, size. |
| `pack` | Builds a `.macroDeckPlugin` artifact, validating the manifest first and recomputing `files[]` digests. |
| `test` | Runs the conformance suite and writes a text, JSON or Markdown report. |

### Packing a release

```bash
dotnet build -c Release
```

```bash
macrodeck-plugin validate --manifest src/ETS2Telemetry/bin/Release/net10.0/manifest.json
```

```bash
macrodeck-plugin pack --source src/ETS2Telemetry/bin/Release/net10.0
```

```bash
macrodeck-plugin inspect --artifact <id>-<version>.macroDeckPlugin
```

`pack` validates before it writes, so a bad manifest never becomes an artifact. It discards whatever
`files[]` the source manifest declared and recomputes every digest from disk. It cannot sign anything:
sign *after* packing, against the packed manifest, or the digest will not match.

`--output` defaults to `<id>-<version>.macroDeckPlugin`; `--force` overwrites an existing file.

### Conformance

```bash
macrodeck-plugin test --project src/ETS2Telemetry --report markdown --output conformance.md
```

The suite drives a real session against your plugin: capability contracts, invocation and cancellation
semantics, reconnect and resume behaviour, the reserved `/_macrodeck/*` endpoints, and logging limits.
Checks are Required or Recommended, each with a stable id (`MDC0401`, …) you can select with `--check`
or `--category`. A check can report `SKIP` with a reason when your plugin gives it nothing to observe -
which is most of them until you add capabilities.

Exit codes make it usable as a CI gate - `0` conformant, `1` the plugin is wrong, `2` usage error, `3`
input unreadable, `4` cancelled. `1` and `3` are deliberately distinct: a missing file is an
environment problem, not a verdict about the plugin.

## Testing

```bash
dotnet test
```

The test project references `MacroDeck.Plugin.Testing`, which provides a loopback test host, fakes and
assertions for testing a plugin without a running Macro Deck. `PluginTestHarness.Create` builds your
plugin from the same `Action<PluginHostBuilder>` `Program.cs` uses - no socket, no host, no built
executable - with a `ManualTimeProvider` for the clock and a `FakeIntegrationContext` you can seed and
assert against:

```csharp
await using var harness = PluginTestHarness.Create(builder => builder.RegisterIntegration<PluginIntegration>());
await harness.InitializeIntegrationsAsync();
```

Drive capabilities through the typed clients it exposes (`harness.Actions`, `harness.Variables`, …)
rather than calling an executor directly, so parameter binding is under test too.
`MacroDeckTestHost.HostAsync` puts the wire itself under test, and `MacroDeckTestHost.LaunchAsync`
runs a real child process.

The conformance suite above covers the protocol contract; these tests are for your own behaviour.

## Contributing to the template

The template repository's root *is* the `dotnet new` content, so changing the template is an ordinary
change to the plugin in `src/`. How the package is built and released is documented in
[`packaging/README.md`](https://github.com/Macro-Deck-App/Macro-Deck-Plugin-Template/blob/main/packaging/README.md).

## License

MIT - see [LICENSE](LICENSE). Macro Deck itself is licensed under Apache 2.0.

## Further reading

- [Plugin development docs](https://github.com/Macro-Deck-App/Macro-Deck-3/tree/main/docs/plugin-development)
- [Sample plugins](https://github.com/Macro-Deck-App/Macro-Deck-Sample-Plugins) - a worked example per capability
- [`plugin-hosting.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/plugin-hosting.md) - the builder API, registration modes, the artifact format and every `MACRO_DECK_PLUGIN_*` variable
- [`sdk-reference.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/sdk-reference.md) - every interface and record you build against
- [`cli.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/cli.md) - every CLI command and option
- [`testing-plugins.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/testing-plugins.md) - the test harness, the fakes and the manual clock
- [`conformance.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/conformance.md) - the conformance suite and its check ids
- [`analyzers.md`](https://github.com/Macro-Deck-App/Macro-Deck-3/blob/main/docs/plugin-development/analyzers.md) - the compile-time diagnostics
