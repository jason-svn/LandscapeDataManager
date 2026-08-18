# Session Context — WWP.LandscapeDataManager

This file summarizes a long Claude Code session on this repo, so another agent (Codex) can pick up
work without re-deriving the history. **Nothing described below has been committed yet** — see
"Current repo state" at the bottom for the full uncommitted diff list.

## What this repo is

A Revit add-in (.NET 8, C#) called LIM (Landscape Information Manager) for WWP/EGIS. Architecture:

- One Revit-connector assembly, `WWP.LandscapeDataManager.Revit` (`IExternalApplication`), adds a
  ribbon tab ("LIM") with buttons that launch standalone tool executables.
- 11 standalone WinUI3 tool exes (`WWP.LandscapeDataManager.App.*` projects) — each is its own
  process, communicating with the Revit connector over named pipes (`RevitPipeServer`/
  `RevitPipeClient`, JSON request/response via `PipeCommands` in
  `WWP.LandscapeDataManager.Contracts\PipeProtocol.cs`).
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

There used to be a 12th project, the companion `WWP.LandscapeDataManager.App` — an older, tabbed
all-in-one tool that predated the individual tools above. **It was deleted this session** (dead
code — fully superseded, and its ribbon-launch path had zero live callers). See item 12 below.

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
   11 tool exes (plus the companion App, before it was deleted — see item 13).
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
11. **Dashboard rebuilt as a graphic KPI report** — three `Pivot` tabs (Executive overview, Design
    scenarios, Data explorer), a 5/10/20/25-year "Outlook" selector that multiplies current annual
    stored results, a local JSON snapshot cache (`DashboardSnapshotCache`, so the tool still opens
    with last-known data when Revit's pipe is unavailable), and PNG/SVG/Excel export
    (`DashboardSvgExportService` is a hand-built vector renderer, not a screen capture). Polished for
    visual consistency afterward (status-card color coding, table headers, header/filter styling).
12. **UI-tested every tool with `winapp` (win-dev-skills plugin)** — installed the `winapp` CLI
    (`winget install Microsoft.WinAppCli` — note the package ID is `WinAppCli`, not `WinAppCLI`),
    then launched every standalone exe with a dummy `--pipe` argument and screenshotted it. Found
    and fixed two real launch-crashing bugs that `dotnet build` never caught (WinUI3 XAML failures
    are native `XamlParseException`s that fail-fast with **no dialog, no log** — diagnosing them
    required temporarily adding an `UnhandledException` handler to each `App.xaml.cs` that writes
    the exception to `%TEMP%\<tool>-crash.txt`; kept permanently as a low-cost safety net):
    - **Dashboard**: crashed on every launch. Root cause was the 🌿 emoji in the header badge (an
      astral-plane Unicode character — WinUI3's XAML compiler/`.pri` resource pipeline has known
      fragility around surrogate pairs) compounded by a stale incremental build. Fixed by swapping
      the emoji for a plain "LIM" text badge and doing a clean rebuild.
    - **Companion App** (before its removal — item 13): crashed with `Cannot find a Resource with
      the Name/Key LimMutedTextStyle`. Its `App.xaml` never merged the shared
      `SharedUI\Styles.xaml` dictionary that every other tool merges — a pre-existing gap, not
      something introduced this session.
    - All other 9 tools (Settings, Parameters, ITreeDownloader, ITreeCalculator, SyncAudit,
      TreeSearcher, LocationFinder, FloorCalculator, Importer) launched and rendered correctly on
      the first try.
    - **Gotcha for next time**: `winapp ui screenshot -a <PID>` composites every window owned by
      the target process (WinUI3 apps have small invisible utility popup windows), producing an odd
      tiled image. Doesn't matter much — the main window still renders correctly inside the
      composite — but a truly single-window capture doesn't seem to be selectable via `-w <HWND>`
      alone.
13. **Companion App deleted entirely** (explicit user direction: "that multi tab interface is the
    deprecated code, it should have been deleted") — confirmed first that it was genuinely dead
    (its `OpenWorkflowCommand` ribbon-launch path had zero concrete subclasses anywhere, and no
    test references its internals), then removed: the whole `src\WWP.LandscapeDataManager.App\`
    project folder, `Revit\Infrastructure\CompanionLauncher.cs`, the now-empty `OpenWorkflowCommand`
    base class in `OpenWorkflowCommands.cs`, the `CompanionLauncher` property/init/dispose in
    `Revit\App.cs`, its `ProjectReference` and the `-AppAssembly` deploy arg in
    `WWP.LandscapeDataManager.Revit.csproj`, its `Project(...)`/`EndProject` block and 8
    `ProjectConfigurationPlatforms` lines in the `.sln`, its App-deploy block in
    `Deployment\Deploy-DebugConnector.ps1`, and its list entry in `Package-Addin.ps1` /
    `Install-Addin.ps1`. Also deleted the stale `artifacts\App\` and deployed
    `...\Addins\2025\WWP.LandscapeDataManager\App\` folders.
    - **Pre-existing, unrelated to this removal**: `Package-Addin.ps1` and `Install-Addin.ps1`'s
      `$toolApps` lists were already missing TreeSearcher/LocationFinder/FloorCalculator/
      HealthCheck/Dashboard/Settings even before this session — they only ever covered
      App/Parameters/Importer/ITreeDownloader/ITreeCalculator/SyncAudit. Not touched/fixed here,
      just noted since it means neither script currently packages/installs the full tool set.

14. **Audited every tool for leftover credential/API-settings UI, then centralized the one real
    gap.** User request: "there is still some tools with api settings... they should all be in the
    setting app not in the tool itself." A dedicated audit subagent confirmed the i-Tree API key and
    Airtable personal-access-token are already fully centralized everywhere (every tool shows only a
    read-only "…saved (managed in Settings)" status + a Settings button — no tool lets you type a
    secret directly). The one real gap: `App.Importer` and `App.SyncAudit` both had their own
    editable "Base ID" / "Table name" / "View" text boxes for the Airtable *planting-data* source
    (`AirtableApiSettings` — distinct from `WwpLdsSource`, which Settings already owned exclusively).
    Fixed by adding a new "PLANTING DATA SOURCE (AIRTABLE)" section to Settings (mirrors the existing
    WWP LDS SOURCE section — Base ID/Table/View + Save, pushes `airtableApi` via
    `ProjectSettingsSync.PushAsync`), then converting Importer and SyncAudit's own Base/Table/View
    boxes into a single read-only status line ("Source: base {BaseId} / table {TableIdOrName}
    (managed in Settings)") — same pattern `FloorCalculator.SourceStatusText` already used for
    `WwpLdsSource`. Importer's `Connect_Click` no longer pushes `airtableApi` (Settings owns the only
    write path now); both tools still push `dataSource`/`preferredUnitSystem` themselves, and Excel
    workbook path selection stays untouched in each tool (that's a one-off browse choice, not a
    durable setting, same reasoning as the shared-parameter-file path).

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
- Every tool has now been launch-tested standalone (dummy `--pipe`, `winapp ui screenshot` — see
  item 12) and confirmed to open and render without crashing. **None of it has been tested against
  a live Revit session yet** — no real pipe connection, no actual model data, no "Refresh
  model"/data-fetch flow, no export-to-file flow. Worth a manual pass through Settings → Floor
  Calculator → Dashboard → Excel export with an actual Revit project open before considering this
  done.

## Current repo state

The bulk of this session's earlier work (items 1–10 above) has already been committed at some
point — `git status --short` now shows only the most recent chunk (items 11–13: the Dashboard
crash fix + polish, and the companion App's complete removal): `CONTEXT.md`, 4 deploy
scripts/`.sln`, the Dashboard's `App.xaml.cs`/`MainPage.xaml`, 3 modified Revit-connector files,
and 11 deleted files under `src\WWP.LandscapeDataManager.App\` plus `Infrastructure\
CompanionLauncher.cs`. Run `git status`/`git diff` to see the exact current picture before
committing — this file is a narrative summary, not a substitute for reviewing the actual diff.

`dotnet build WWP.LandscapeDataManager.sln -c Debug` succeeds with 0 warnings/0 errors and deploys
all 11 tools + the connector (no more companion App deploy step). `dotnet test
tests\WWP.LandscapeDataManager.Tests\...` passes 72/72. Every tool has also been launch-tested
standalone via `winapp` and confirmed to render without crashing (see item 12) — but not yet
against a live Revit session.
