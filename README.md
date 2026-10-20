# Lumin Client Bedrock

A Minecraft Bedrock client project built with Codex.

Lumin is a Windows Presentation Foundation application for a modular Minecraft Bedrock client experience. The first scaffold is a standalone UI preview: the module catalog and switches update local application state, while game integration is explicitly unavailable.

## Requirements

- Windows 10 or newer
- .NET 10 SDK

## Run

From this directory:

```powershell
dotnet run --project .\Lumin.csproj
```

To publish a Windows executable:

```powershell
dotnet publish .\Lumin.csproj -c Release -r win-x64 --self-contained false
```

Run the built-in offline checks with:

```powershell
dotnet run --project .\tests\Lumin.Tests.csproj
```

## Current scope

- Lumin, Rain, and Synth are selectable profile themes backed by independent resource dictionaries in `Themes/`.
- Lumin uses monochrome glass and icy blue-white light; Rain uses storm-slate and aqua; Synth uses deep violet with neon rose accents.
- The module browser supports name, category, and description search, state filters, module selection, and working local toggles.
- The module catalog is centralized in a registry with stable identifiers and explicit availability labels.
- Module states and interface scale persist in named profiles under the current user's local application data.
- Profiles can be duplicated, deleted, imported, and exported. Imported JSON is size-limited, versioned, validated, and never executed.
- Schema version 1 configurations migrate to version 2 on load.
- The optional Activity Log records local Lumin profile/module changes to JSONL and can export them; it does not capture Minecraft traffic or game state.
- Dashboard counters and quick access switches reflect the active profile.
- No Minecraft APIs or live-game capabilities are claimed by this preview.
- Theme selections are saved per profile and apply immediately without changing module state.

## Theme notes

`Themes/Lumin.xaml` owns grayscale environment tokens and the restrained icy module glow. `Themes/Controls.xaml` contains reusable navigation, glass card, search, filter, and toggle styles. Translucent surfaces use enough charcoal opacity to preserve readability; if a future host environment cannot composite transparency, these colors remain readable as solid smoke-gray surfaces.