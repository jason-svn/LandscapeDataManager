# Handoff, 2026-10-26 (Claude Code → Codex on another PC, until Wednesday 2026-10-28)

## Setting up on another PC

1. Clone these repos side by side:
   - `jason-svn/LandscapeDataManager`, branch `roadmap-phases-2-5` (v1.9.1).
   - `jason-svn/LandscapeCoBenefitsDashboard`, branch `handoff-2026-10-26`.
   - `jason-svn/LIM-agent-context` (private): Claude's working notes on the user's rules and past
     decisions, plus scratch tools (export harness for the web sample, headless web test, Revit
     template scripts). Read its `README.md` when you need more than this file.
2. Install the .NET 8 SDK. Revit 2025 is needed only to build and install the connector
   (`Install-Addin.ps1`); the tests and tools build without it.
3. Airtable work needs the token on that PC: LIM Settings saves it to Windows Credential Manager.
4. Optional, if Revit runs on that PC: give Codex the pyRevit MCP server in `~/.codex/config.toml`:

   ```toml
   [mcp_servers.pyrevit]
   command = 'C:\Program Files\pyRevit-Master\bin\pyrevit.exe'
   args = ["mcp"]
   ```

5. Start Codex inside `LandscapeDataManager`, so it loads `AGENTS.md`. First prompt: "Read AGENTS.md
   and HANDOFF.md, then start at Next steps 1."

## Where things stand

- **LIM v1.9.1** is pushed on `roadmap-phases-2-5` but not released. It builds and all 566 tests
  pass. It has **not been installed or tried in Revit**: Revit was open, so the connector couldn't
  be deployed.
- **Web dashboard**: branch `handoff-2026-10-26` holds commit `462a07e` (3D across the panel, result
  board under it) and the phenology panel with the sample. **`master` (the live GitHub Pages site)
  is unchanged.** Merge to `master` only when the user asks.

## What v1.9.0 adds (v1.9.1 only tightened these handoff notes)

1. **New Revit tree types → Airtable.** An Importer card, "ADD NEW TREE TYPES TO AIRTABLE", creates
   sheet rows for tree types the sheet doesn't have yet, so people can add the non-i-Tree data.
   Files: `AirtableApiClient.CreateRecordsAsync` (batches of 10, typecast; 403 →
   `AirtableInsufficientScopeException`), `Shared/Services/NewTreeTypes.cs`,
   `App.Importer/NewTreeTypeRow.cs` and `MainPage.xaml(.cs)`.
2. **Floor matches.** The Importer pins each floor type to the sheet row Floor Calculator matched
   it to (`FloorMatchAliases`). The type scan covers every design option. `StableTypeMatcher` narrows
   a species code shared by several rows by type name.
3. **Plant colours.** 8 new type params,
   `!_S_PLT_iTreeSpecies_{FlowerColour,FruitMonths,FruitColour,FruitType,SpringColour,SummerColour,AutumnColour,WinterColour}_Text`.
   - The connector reads them into `DashboardTreeItem.Appearance`; parsing is in `PlantColours.cs`.
   - Desktop Dashboard: the phenology table has flower-colour dots, a fruit mark, and each month
     cell shaded in its season colour.
   - Web export: schema 11.
   - The Airtable rows are already prefilled with these colours (researched, then reviewed).
4. **Money in the project currency.** The Importer, Floor Calculator and Tree Calculator read the
   currency a cell names ("£20", "US$10", "€8") or its Airtable currency field, and convert at the
   project's rate.
   - Code: `Money.cs` (`Money`, `CurrencyFactors`), `ImportMoney.cs`,
     `AirtableApiClient.GetFieldCurrenciesAsync` and `WwpLdsSheet.CurrencyFactorsAsync` /
     `CostSavedIn`.
   - Reading a currency field's symbol needs the token's `schema.bases:read` scope; without it, LIM
     quietly reads the cells alone.
   - A value with no exchange rate is skipped with a reason, never written unconverted.

## Next steps, in order

1. On the PC with Revit: close Revit and run `./Install-Addin.ps1` (connector plus every tool; the
   window titles should say 1.9.1).
2. In Revit: in the LIM Parameters tool, add the 8 plant-colour params; in the Importer, sync from
   Airtable; check the desktop Dashboard phenology table.
3. Check the currency conversion in a USD project. The Importer should turn a maintenance cost of
   "£20.00" into about 26.7 USD and show the rate in the row message. Floor Calculator's cost saved
   should convert too.
4. Re-export the web sample (schema 11) from the Dashboard into
   `LandscapeCoBenefitsDashboard/sample-data/example-export.json` (it is schema 10 now), then check
   the web phenology panel with the new colours.
5. Only when the user asks: merge the web branch into `master` (this publishes the site), and
   release LIM.

## Open questions for the user (Revit template "LIM Project")

The template was brought up to date from the sandbox on 2026-10-26 (filters, views, legends, live
schedules on sheets, params, settings) and checked by diff. Make sure it was saved.

- Template currency: USD/Imperial or GBP/Metric?
- Delete the old KPI "Variante 1" schedules?
- Delete the unused `WWP_CO2_Sequestration_*` filters?
- Copy the Baseline plan and the `00_SIT_Baseline` view template?

## Background worth knowing

- **Web 3D:** LIM exports a compact scene and the web page rebuilds it with three.js. Trees are
  InstancedMesh (trunk plus crown, from the family's sizes); floors are their triangulated top faces.
  Growth is automatic, and each scenario's elements come from the same filter as its totals.
- **"Colour by"** uses Revit's current values: every view is scanned, and Revit's `PassesFilter`
  decides each element.
- **Stored i-Tree timelines** from before 2026-10-23 have no sizes and must be treated as stale.
- **The Airtable sheet** is the source for bloom months: flowering months only, after the user's
  colleague cleaned them up.
