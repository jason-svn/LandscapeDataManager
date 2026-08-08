# Session Context — WWP.LandscapeDataManager

This file summarizes a long Claude Code session on this repo, so another agent (Codex) can pick up
work without re-deriving the history. **Nothing described below has been committed yet** — see
"Current repo state" at the bottom for the full uncommitted diff list.

## What this repo is

A Revit add-in (.NET 8, C#) called LIM (Landscape Information Manager) for WWP/EGIS. Architecture:

- One Revit-connector assembly, `WWP.LandscapeDataManager.Revit` (`IExternalApplication`), adds a
  ribbon tab ("LIM") with buttons that launch standalone tool executables.
- ~11 standalone WinUI3 tool exes (`WWP.LandscapeDataManager.App.*` projects, plus the original
  companion `WWP.LandscapeDataManager.App`) — each is its own process, communicating with the Revit
  connector over named pipes (`RevitPipeServer`/`RevitPipeClient`, JSON request/response via
  `PipeCommands` in `WWP.LandscapeDataManager.Contracts\PipeProtocol.cs`).
- `WWP.LandscapeDataManager.Shared` holds cross-tool services (Airtable/Excel clients, SQLite
  caches, unit conversion, dashboard aggregation, etc.) referenced by both the Revit connector and
  the tool exes.
- Tests live in `tests\WWP.LandscapeDataManager.Tests` (xUnit, currently 72 tests, all passing).

### The tools (current ribbon display names)

| Ribbon panel | Display name | Project | Purpose |
|---|---|---|---|
| Project Setup | Settings | App.Settings | Single place for all API keys/tokens/settings |
| Project Setup | Import Shared Parameter | App.Parameters | Imports/binds the shared parameter file |
| Project Setup | iTree Database Cacher | App.ITreeDownloader | Downloads/caches the i-Tree species catalogue |
| Project Setup | Location Finder | App.LocationFinder | Map/address picker → project site location |
| Data Processing | Tree Searcher | App.TreeSearcher | Search species catalogue, assign to Planting |
| Data Processing | Excel Importer | App.Importer | Map Airtable/Excel columns → Revit parameters |
| Data Calculation | Tree Calculator | App.ITreeCalculator | Calls i-Tree API, writes tree benefit results |
| Data Calculation | Floor Calculator | App.FloorCalculator | Matches Floors to WWP landscape data sheet, computes benefits |
| Diagnosis | Refresh and Audit | App.SyncAudit | 3-way diff (last-synced/current/latest-source) |
| Diagnosis | Health Check | App.HealthCheck | Scans for missing/stale data, colors the view |
| Reporting | Benefits Dashboard | App.Dashboard | Rolls up stored results into subtotals/grand totals |

The companion `WWP.LandscapeDataManager.App` project is an older, tabbed all-in-one tool that
predates the individual tools above; it's still wired up but most new work happens in the
individual tools.

Renamed this session (ribbon button text + tooltip + tool window title + page title, display-only,
**no internal project/exe/namespace/command-class renames**):
- "Project Setup" → **"Import Shared Parameter"** (still `SetupParametersCommand`, still the
  `App.Parameters` project/exe)
- "i-Tree Downloader" → **"iTree Database Cacher"** (still `DownloadSpeciesScheduleCommand`,
  `App.ITreeDownloader`)
- "i-Tree Calculator" → **"Tree Calculator"** (still `CalculateITreeCommand`, `App.ITreeCalculator`)

## Key conventions to know before editing

- **Shared parameter file** (`Shared_Parameters_WWP.txt`, repo root) is **UTF-16LE with BOM, CRLF**
  line endings, tab-separated `PARAM <GUID> <Name> <DATATYPE> ... <description> ... ` rows. **Never
  edit it with a text-editing tool** (it will corrupt the encoding) — always use PowerShell:
  `Get-Content -Encoding Unicode -Raw` to read, `[System.IO.File]::WriteAllText(path, content,
  [System.Text.Encoding]::Unicode)` to write. Verify afterward with `file Shared_Parameters_WWP.txt`
  (should report "UTF-16, little-endian ... with CRLF line terminators") and grep the decoded text
  via `iconv -f UTF-16LE -t UTF-8`.
- **Parameter naming**: all custom parameters use the `!_S_PLT_` prefix (renamed this session from
  an earlier `!_S_PLANTING_` / `WWP_` scheme). Suffix encodes the Revit data type:
  `_Text`, `_Number`, `_Mass` (kg, Imperial-aware via `UnitTypeId.Kilograms`), `_Volume` (m³, via
  `UnitTypeId.CubicMeters`), `_Currency` (money, via `SpecTypeId.Currency` + a locale-based symbol
  set on the project preferences).
- **Retyping an existing parameter** (e.g. Number → Mass) always means: new GUID, delete the old
  PARAM line, add `legacyAliases: ["<old name>"]` to the `ParameterOwnership` entry in
  `SharedParameterSetupService.cs` so `RemoveStaleBindings` cleans up the old binding automatically
  next time "Import Shared Parameter" runs. Never reuse a GUID across a type change.
- **`ProjectSettingsSync`** (`Shared\Services\ProjectSettingsSync.cs`) is the *only* persistence
  path for non-secret settings — there are **no local `%LOCALAPPDATA%` cache files anymore** (they
  were deliberately removed this session). Everything round-trips through the Project Information
  parameter `!_S_PLT_Settings_Json_Text` via `ProjectSettingsSnapshot`. Pattern for any tool:
  `var snapshot = await ProjectSettingsSync.PullAsync(GetClient());` on load, `await
  ProjectSettingsSync.PushAsync(GetClient(), dataSource: ..., airtableApi: ..., ...)` on save —
  pass only the fields you're updating, the rest merge with what's already stored.
  - **Explicitly out of scope for this sync**: credentials (`ITreeCredentialStore`,
    `AirtableCredentialStore` — Windows Credential Vault, never written to Project Information) and
    bulk SQLite caches (`SyncedValueHistoryStore`, `SpeciesCatalogueDatabase`,
    `WwpLdsCoefficientDatabase` — too large for a single text parameter, not "settings").
- **Settings button on every tool**: every standalone tool has an "Settings" button
  (`OpenSettings_Click`) that calls `SiblingToolLauncher.ShowOrStart(Path.Combine("Settings",
  "WWP.LandscapeDataManager.Settings.exe"), _pipeName)` — a show-or-focus-existing-window pattern
  (new file this session: `Shared\Services\SiblingToolLauncher.cs`). Every tool launches the *same*
  Settings.exe instance rather than spawning duplicates.
- **Revit lock**: always check `tasklist /FI "IMAGENAME eq Revit.exe"` before a Debug-config build —
  Revit locks the add-in DLLs while running.
- Building the whole solution (`dotnet build WWP.LandscapeDataManager.sln -c Debug`) also deploys
  every tool + the connector to `%APPDATA%\Autodesk\Revit\Addins\2025\WWP.LandscapeDataManager\`.

## What happened this session, roughly chronologically

1. **Benefits Dashboard tool built** (`App.Dashboard`) — reports accumulated i-Tree/landscape-data-
   sheet results already stored on Planting and Floor instances: per-species subtotals, per-floor-
   type subtotals, a grand total, filterable by Design Option/level/status, Metric/Imperial +
   currency aware, exportable as PNG or an Excel workbook (`DashboardExcelExportService`).
2. **Airtable source pointer fixed** — the app was pointed at a stale duplicated Airtable view;
   updated the default (`WwpLdsAirtableSettings.CompanyDefault`) to the correct view.
3. **Parameter rename cleanup** — fixed stale `!_S_PLANTING_*` parameters still appearing after an
   earlier rename to `!_S_PLT_*` (root cause: name-only lookup in binding search, not GUID-aware).
4. **Settings tool created** (`App.Settings`) — centralizes every API key/token/source setting that
   used to live scattered across tools.
5. **i-Tree download parameter retyping** — Number-typed parameters that were actually
   Volume/Mass/Currency got proper Revit specs (new GUIDs, old ones deleted, `legacyAliases` added).
   Confirmed via live reflection against `RevitAPI.dll` that Revit *does* have a Mass spec
   (`SpecTypeId.Mass`/`UnitTypeId.Kilograms`) — earlier assumption that it didn't was wrong.
   Shared-parameter tooltips rewritten to be human-readable. Currency symbol now follows locale
   (`ProjectPreferencesService.SetCurrencySymbol`, e.g. GBP → `SymbolTypeId.UkPound`).
6. **Local settings caches removed entirely** — see `ProjectSettingsSync` convention above. This
   touched almost every tool's `Page_Loaded`/save handlers and deleted several `*Store` classes
   (`DataSourceSettingsStore`, `AirtableApiSettingsStore`, `ParameterMappingStore` [deleted file],
   `TypeAliasStore`, `WwpLdsAirtableSettingsStore`, `SharedParameterFileSettingsStore` — these now
   contain only the data *records*, no file I/O). Also removed the manual "Export/Import settings
   to a file" buttons that existed as a workaround before auto-sync (redundant now).
7. **Settings button added to every tool** (see convention above) — mechanical rollout across all
   11 tool exes plus the companion App.
8. **Auto-map fix + expansion** (companion App's `GetTargetCandidates`, used by the "Sync & Review"
   tab's column-mapping auto-suggest) — one entry (`"Maintenance Costs"`) was pointing at a deleted
   parameter name; fixed to the current `!_S_PLT_LDS_MaintenanceCostAnnual_Currency`. Cross-checked
   all literal Airtable header strings against a real CSV export of the WWP landscape data sheet
   (`Dataset-DATASET (2).csv` in the user's Downloads — a backup of Airtable base
   `apptELCzLzMbmrk54`) and added ~9 more aliases for headers that were previously falling through
   to "map manually" (Origin, WWP_LDS_Category/SubCategory, Product/Transport GWP, Pollen,
   Surface/Air temp reduction, Irrigation demand, WWP_Pollutants_Removed). Also made the switch
   trim whitespace from source header names for robustness.
9. **Tool renames** — see table above.
10. **Dashboard "Floor" → "Planting Area" rename + missing benefit metrics** — investigated why
    Floor Calculator's benefits felt incomplete: it *was* calculating Runoff Avoided but the
    Dashboard never displayed it, and Pollution Mitigated wasn't being calculated **at all** even
    though its shared parameter already existed (bound but never written to). Fixed both:
    - Added `PollutionMassRemovedAnnual` end-to-end: WWP landscape data sheet column
      `WWP_Pollutants_Removed` → `WwpLdsCoefficientRecord`/`WwpLdsCoefficientDatabase` (new SQLite
      column + migration) → Floor Calculator's `FloorAssignmentRow` (new 8th metric, "Pollution
      Mitigated") → `FloorLdsValues` → `FloorLdsCalculationService` (writes to Revit).
    - Retyped `!_S_PLT_LDS_PollutantsRemovedAnnual_Number` → `..._Mass` (new GUID
      `a9b89673-22d6-4b7a-bde3-2046ce5d38b0`, legacy alias added) so it's Imperial-aware like its
      Oxygen/TotalGWP siblings.
    - Wired `RunoffAvoidedAnnual` (already calculated, just never surfaced) and the new
      `PollutionMassRemovedAnnual` through `DashboardReportService.CreateFloorItem` →
      `DashboardFloorItem` contract → `DashboardAggregationService` (`FloorTypeSubtotal`,
      `DashboardGrandTotal`) → Dashboard UI (`DashboardRow.cs`, new KPI card "PLANTING AREA RUNOFF /
      POLLUTION", new subtotal-table and instance-list columns) → `DashboardExcelExportService`.
    - Renamed every user-facing "Floor"/"Floor-based plants" string in the Dashboard tool to
      "Planting Area" (titles, KPI labels, table/column headers, Excel sheet name and row labels).
      Did **not** rename the "Floor Calculator" tool itself, or any internal C# type/property names
      (`DashboardFloorItem`, `FloorSubtotalRow`, etc.) — display text only.

## Known gaps / things not done (candidates for follow-up)

- `!_S_PLT_LDS_MaintenanceCostAnnual_Currency` has the *exact same* gap `PollutionMassRemoved` had:
  the shared parameter exists (bound to Planting+Floor instances) but Floor Calculator never
  calculates/writes it — there's no `MaintenanceCostAnnual` field anywhere in
  `WwpLdsCoefficientRecord`/`FloorLdsValues`/`FloorAssignmentRow`. Flagged but intentionally not
  fixed this session (out of the explicit ask) — same fix pattern as Pollution Mitigated would
  apply if wanted, reading a `"Maintenance Costs"` coefficient from the WWP sheet.
- Floor/Planting-Area dashboard values are **not** unit-system-normalized the way tree values are
  (`DashboardAggregationService.NormalizeTree` handles Metric/Imperial + currency conversion for
  trees; floors are summed as-is and always display fixed units — "kg", "m³", "m²" — regardless of
  the dashboard's unit-system toggle). Pre-existing behavior, not introduced or fixed this session.
- None of this session's work has been manually verified inside Revit yet (no live model testing) —
  only `dotnet build`/`dotnet test` were run. Worth a manual pass through Settings → Floor
  Calculator → Dashboard → Excel export before considering this done.

## Current repo state

**Nothing has been committed.** `git status --short` at the end of this session shows the working
tree modified across ~50 files plus one new file (`Shared\Services\SiblingToolLauncher.cs`) and one
deleted file (`Shared\Services\ParameterMappingStore.cs`). Run `git status`/`git diff` to see the
full picture before committing — this file is a narrative summary, not a substitute for reviewing
the actual diff.

`dotnet build WWP.LandscapeDataManager.sln -c Debug` succeeds with 0 warnings/0 errors and deploys
all tools + the connector. `dotnet test tests\WWP.LandscapeDataManager.Tests\...` passes 72/72.
