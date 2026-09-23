# ETS2 Telemetry — MD3, v2 (po weryfikacji przez dekompilację SDK)

## Co się zmieniło od poprzedniej wersji

Dzięki zdekompilowanemu `MacroDeck.Sdk.dll` (3.0.0-preview.3) wszystkie sygnatury poniżej są
teraz **pewne**, nie zgadywane:

- `IPluginIntegration` = `Actions` + `InitializeAsync(IIntegrationContext)` + `ShutdownAsync()`
- `IVariableProvider` = `ProvidedVariables` (typu `ProvidedVariable`, nie `VariableDefinition`!)
  + `GetValueAsync(string name, CancellationToken)`
- `IConfigFlowProvider.CreateConfigFlow()` → `IConfigFlow` z `StartAsync`/`SubmitAsync` —
  dodałem prawdziwy config flow dla `UseMph` w `ConfigFlow/Ets2ConfigFlow.cs`, zamiast placeholdera

## Jedna rzecz wciąż niepewna: trwałość configu

`InitializeAsync` czyta `context.Config.GetEntriesAsync()` i pierwszy wpis traktuje jako "nasz"
config. To rozsądne założenie przy jednym, prostym pluginie bez `AllowsMultipleConfigurations`,
ale nie widziałem przykładu użycia tego API w praktyce (np. co się dzieje, zanim użytkownik
w ogóle przejdzie przez config flow pierwszy raz — czy `GetEntriesAsync()` zwraca pustą listę,
czy wpis z pustymi wartościami). Kod obsługuje pusty przypadek (`FirstOrDefault()` może być
`null`), ale przetestuj to jako pierwsze po buildzie.

## Build

Podmień pliki w `src/ETS2Telemetry/` na te z tego folderu (w tym nowy `ConfigFlow/`), potem:

```
dotnet build
```

Jeśli są błędy — wklej je, poprawimy dalej tą samą metodą (masz teraz `ilspycmd` zainstalowane,
więc przy kolejnych niejasnościach możemy sami zdekompilować dowolny inny plik SDK).

## Test

`macrodeck-plugin run --project .` albo profil "Macro Deck - Real Host" — sprawdź:
1. `Session established` w konsoli
2. Akcje "Start telemetry" / "Stop telemetry" widoczne w Macro Deck
3. Zmienne `speed`, `fuel_percent`, `gear`, `speed_limit` pojawiają się w liście zmiennych
4. Przycisk "Configure" przy pluginie otwiera krok z przełącznikiem "Use mph"
