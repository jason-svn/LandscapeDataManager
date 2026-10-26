# LIM (WWP.LandscapeDataManager): agent notes

Read `HANDOFF.md` next: it has the current state, what still needs checking in Revit, and the next
steps. `docs/ROADMAP.md` (Chinese copy `docs/ROADMAP.zh-CN.md`, kept in step) is the source of truth
for the plan. `CONTEXT.md` is an older session summary (2026-10-22), background only.

Before you end a session, update `HANDOFF.md` (what changed, what was checked, next steps), then
ask the user whether to commit and push it. That push is how the next agent, on any machine, picks
the work up; like every commit, it happens only when the user says so.

## Working with the user

- Reply in Simplified Chinese. Code, code comments, commit messages and docs stay in English; keep
  technical names (parameters, paths, buttons) as they are.
- Commit, push, release or deploy only when the user asks.
- Never print secrets. LIM keeps the Airtable token in Windows Credential Manager (resource
  `EGIS.WWP.LandscapeDataManager.Airtable`, user `AirtableApi`), set from LIM Settings. Read it
  in-process; never echo it.
- Don't edit text files with Windows PowerShell 5.1 `Get-Content`/`Set-Content` (it corrupts UTF-8);
  use node, Python or an editor. `Shared_Parameters_WWP.txt` is UTF-16 LE with BOM and CRLF:
  preserve both.

## Layout, build, test, deploy

- .NET 8. Revit 2025 connector `src/WWP.LandscapeDataManager.Revit` (named pipe
  `WWP.LandscapeDataManager.{RevitPID}`); 11 WinUI 3 tool exes `src/WWP.LandscapeDataManager.App.*`;
  shared code `src/WWP.LandscapeDataManager.Shared`; pipe contracts
  `src/WWP.LandscapeDataManager.Contracts/PipeProtocol.cs`.
- Tests: `dotnet test tests/WWP.LandscapeDataManager.Tests` (566 pass at v1.9.0).
- One tool: `dotnet build src/WWP.LandscapeDataManager.App.<Name>`.
- Deploy everything: `./Install-Addin.ps1`. It needs Revit 2025 installed, and Revit must be closed
  (Revit locks the connector DLL). Check with `tasklist /FI "IMAGENAME eq Revit.exe"` (exact match:
  `pyrevit.exe` also contains "revit.exe").
- To try new connector code while Revit is open: copy the built Contracts and Revit DLLs to a
  scratch folder, load them with pyRevit into a collectible `AssemblyLoadContext` (Contracts first),
  invoke the internal service by reflection with `doc`, then unload.

## Versions and releases

- Every push: bump `<Version>` in `Directory.Build.props` first. Minor by default (1.9.0 → 1.10.0),
  patch for a fix-only push. Add a version line to both ROADMAP files.
- Releases go out from `main` (fast-forwarded from `roadmap-phases-2-5`): tag, GitHub release with
  the `Package-Addin.ps1` zip and `Shared_Parameters_WWP.txt`. Only when the user asks.

## Data rules

- Naming: anything LIM calculates is `!_S_PLT_Result_*`, except that `!_S_PLT_iTreeResult_*` stays as
  it is. Sheet rates are `..._Rate_Number`. A rename needs a new GUID and the old name in
  `legacyAliases`. The Importer never writes calculated or retired parameters
  (`DefaultParameterMappingCatalog.IsImportTarget`).
- Keep `!_S_PLT_LDS_MaintenanceCostAnnual_Currency` as it is; the calculated one is
  `!_S_PLT_Result_MaintenanceCostAnnual_Currency`.
- Airtable values are metre-based and Revit displays mm. Convert with
  `UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Meters/SquareMeters/CubicMeters)`, never
  `SetValueString`.
- Money: the WWP sheet is in GBP unless a cell or its column names another currency; LIM converts
  into the project currency (`Money`, `CurrencyFactors`, `ImportMoney`). Maintenance costs are
  annual rates per m². The user confirmed this, so don't flag big maintenance totals.
- Trees are never put in Revit groups. Add no vary-by-group logic; warn when a tree is grouped.
- Airtable: project source base `app0VdTm2LthasJQK` ("LIM-DATASET"), table `tblp9DhoXaKkYKYYp`,
  view "Dataset". Company sheet base `apptELCzLzMbmrk54`, table `tblAwGKQjNQJ9XsKM`.

## UI

- Every data table uses the LIM table language: swim-lane column groups (pale wash, CAPS lane name,
  ~6px gap), two-line headers (name over unit), plain right-aligned numbers. Use `LimTable*Style`
  and `LimLane*Brush` from `SharedUI/Styles.xaml`. The web dashboard tables follow the same language.
- Multi-select lists support Shift+click ranges, with a hint saying so.

## Revit model safety

- Never Synchronize with Central or save to central on a workshared model, and never answer Revit's
  Synchronize dialog. The test model "LAN_LIM Arboris_EWR Sandbox" is a workshared ACC cloud model:
  writes inside transactions are fine.
- In pyRevit's CPython runtime, list comprehensions over .NET objects can fail; use plain loops.
