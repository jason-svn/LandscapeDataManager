# LIM- LANDSCAPE DATA

A `.NET 8` Revit 2025+ connector with a separate WinUI 3 interface for replacing the Dynamo-based landscape data workflows.

- **Quick-start guide:** <https://jason-svn.github.io/LandscapeDataManager/>
- **Download:** [latest release](https://github.com/jason-svn/LandscapeDataManager/releases/latest) (`LIM-Landscape-Data-2025plus.zip`)
- **Online co-benefits dashboard:** <https://jason-svn.github.io/LandscapeCoBenefitsDashboard/> — load a Dashboard **Export JSON** file to view and share the results in a browser ([source](https://github.com/jason-svn/LandscapeCoBenefitsDashboard))

## Architecture

- `WWP.LandscapeDataManager.Revit` runs inside Revit and is the only component allowed to access the Revit API.
- `WWP.LandscapeDataManager.App` is the WinUI 3 companion application.
- `WWP.LandscapeDataManager.Contracts` contains the local request/response contracts shared by both processes.
- Communication uses a per-Revit-process named pipe restricted to the current Windows user.
- Revit API work is marshalled through `ExternalEvent`.

WinUI 3 cannot be inserted directly into a Revit dockable pane because Revit requires a WPF `FrameworkElement`. Keeping WinUI in a separate process also prevents Windows App SDK dependencies from being loaded into Revit.

## Current functionality

- Adds **LIM DATA** under **EGIS > LIM- LANDSCAPE DATA**; i-Tree Excel is integrated as a tab in the WinUI application.
- Opens a `.NET 8` WinUI 3 application with Mica styling.
- Reports the connected Revit version and active document.
- Scans Planting and Floor instances, optionally limiting results to primary design options.
- Groups model elements by Revit type and reports counts, floor area and `WWP_LDS_CalculationType`.
- Loads records from either a public Airtable shared link or a selected Excel `.xlsx`/`.xlsm` workbook.
- Downloads i-Tree benefit data for Revit Planting types through the same API workflow as the original Dynamo graph.
- Creates a new Excel workbook or updates an existing worksheet by matching `Species_Code`.
- Preserves unmatched worksheet rows and user-added columns; only selected i-Tree fields are overwritten on matching rows.
- Can append new species, create a timestamped backup, and export summary groups, timelines, or the full API response.
- Stores the optional remembered i-Tree API key in Windows Credential Manager for the current user.
- Can fetch MyTree's free public i-Tree key directly from the website (Settings > i-Tree API key > **Fetch from MyTree**), so a machine that has never opened i-Tree can obtain a working key with only an internet connection; a client-billed production job should still use an org key issued by Davey.
- Compares normalized Revit type names against the selected source using the current Dynamo field conventions.
- Provides a Parameter Mapper that pairs live source columns with writable Revit instance/type parameters.
- Captures a conversion policy for every mapping and can suggest the known WWP environmental mappings.
- Saves mappings to `%LocalAppData%\EGIS\WWP.LandscapeDataManager\parameter-mappings.json`.
- Keeps all Revit operations read-only. Parameter writes are intentionally not enabled yet.

## Biodiversity Net Gain (BNG)

Floor Calculator's **Biodiversity (BNG)** tab calculates each Floor's habitat units with the Statutory Biodiversity Metric (23.07.2024 release), ported formula-for-formula and pinned to the official workbook by golden test cases (`tools/BngMetricExtractor`). Each floor's Revit phases decide which metric sheet it belongs to, relative to the project's first phase (e.g. *Existing*) and last phase (e.g. *New Construction*):

| Floor's phases | Role | Metric sheet |
| --- | --- | --- |
| Created in the first phase, never demolished | Retained | A-1 baseline |
| …and `!_S_PLT_BNGInput_Enhanced_YesNo` ticked | Enhanced | A-1 + A-3 enhancement |
| Created in the first phase, demolished later | Lost | A-1 baseline |
| Created later, never demolished | Created | A-2 creation |
| Demolished in the phase it was created | Excluded | not counted |

To use it:

1. Run **Import Shared Parameter** so the `!_S_PLT_BNGInput_*` / `!_S_PLT_BNGResult_*` floor parameters are bound (re-run after updating — new parameters only appear once bound).
2. Set each floor's Phase Created / Phase Demolished, and tick **Enhanced** on retained floors whose habitat is improved.
3. In Floor Calculator > **Biodiversity (BNG)**, click **Load all floors**, fill in the baseline habitat and condition for Existing floors, the proposed (or enhanced) habitat and condition for new and enhanced floors, and strategic significance, then **Write to Revit**.
4. Open the **Dashboard**: the Site & Biodiversity tab shows baseline units, post-intervention units (retained + enhanced + created) and the net change against the statutory 10%. Floors whose area, inputs or phases changed since they were written are flagged stale and excluded until recalculated.

Only on-site area habitats are covered — hedgerows, watercourses, off-site units and the trading-rules check aren't modelled.

## Online co-benefits dashboard

In the Dashboard, click **Export JSON**, then open <https://jason-svn.github.io/LandscapeCoBenefitsDashboard/> and drop the file in. The page runs entirely in the browser (nothing is uploaded) and shows the same project as a shareable report: everyday equivalents for the carbon, storm water and air-quality results, a growth-year slider across design options, species and floor breakdowns, and the BNG summary. Exports use schema version 3; older exports still load without the BNG panel.

## Data-source configuration

No Airtable token is required. On **Sync & Review**, choose one of these sources:

- **Airtable shared link**: paste a public base or view share URL. The view must allow CSV downloads.
- **Excel workbook**: select an `.xlsx` or `.xlsm` file. The first populated row supplies the column headers, and the first non-empty worksheet is loaded.

The selected source is saved per Windows user under `%LocalAppData%\EGIS\WWP.LandscapeDataManager\data-source-settings.json`.

## i-Tree Excel export

Select **EGIS > LIM- LANDSCAPE DATA > LIM DATA**, then open **i-Tree Excel** in the WinUI application. Choose all Planting types, the current Revit selection, or the complete catalog; enter the API key used by the Dynamo workflow; and choose a new or existing `.xlsx`/`.xlsm` workbook.

The worksheet header row must contain `Species_Code` when updating existing data. Header and code matching ignore case, spaces and punctuation. Every duplicate matching row is updated. Rows with codes not returned by i-Tree and columns not supplied by the selected export remain unchanged. Missing selected i-Tree headers are appended to the right. Backups and appending newly exported codes are enabled by default.

Choose **Entire i-Tree species catalog (no Revit data required)** to download the complete recognized-species list without scanning Revit or supplying a `Species_Code`. The catalog currently supplies `Scientific_Name`, `Common_Name`, `Species_Code`, `SpeciesType`, and `ReplaceBy`. Calculated benefits are not part of this catalog because they require tree and location inputs.

The default benefit selection uses the established Revit/Dynamo input and output parameter names. Annual timelines, cumulative timelines, and the entire raw response are opt-in because a 20-year full response can produce thousands of columns.

## Installing a release

Download `LIM-Landscape-Data-2025plus.zip` from the [latest release](https://github.com/jason-svn/LandscapeDataManager/releases/latest), extract it and double-click `Install.cmd` (it asks for `2025` or `2026`). Running `.\Deploy-BinaryPackage.ps1` directly from PowerShell fails with "is not digitally signed" on most machines, because Windows blocks unsigned scripts that came from a download; `Install.cmd` runs it with `-ExecutionPolicy Bypass` for that one process, or use `powershell -ExecutionPolicy Bypass -File .\Deploy-BinaryPackage.ps1 -RevitVersion 2025` yourself. It installs for the current Windows user only — no admin rights needed. Restart Revit and open the **LIM** tab.

### Updating

From v1.2.0, the connector checks the [latest GitHub release](https://github.com/jason-svn/LandscapeDataManager/releases/latest) once a day at Revit startup, and **LIM > Project Setup > Check for Updates** checks on demand. Accepting an update opens a PowerShell window that downloads the package while Revit keeps running, waits for every Revit session to close, stops any leftover LIM tool processes, then runs the package's `Deploy-BinaryPackage.ps1`. Installs from v1.1.0 or older have no updater, so they need one manual `Install.cmd` run to reach v1.2.0.

**Publishing a release:** bump `<Version>` in [Directory.Build.props](Directory.Build.props), run `.\Package-Addin.ps1`, then publish a GitHub release tagged `v<Version>` with `artifacts\Packages\LIM-Landscape-Data-2025plus.zip` attached under that exact name. The updater compares the tag against the installed connector's assembly version, so a mismatched tag means installed copies either never see the update or keep being offered it.

### Manual install (when Install.cmd is blocked too)

The script only copies files, so the same can be done by hand. For Revit 2025 (use `2026` throughout for Revit 2026):

1. Before extracting, right-click the ZIP > **Properties** > tick **Unblock** > **OK**, then extract it.
2. Open `%APPDATA%\Autodesk\Revit\Addins\2025` in File Explorer and create a folder named `WWP.LandscapeDataManager`.
3. Copy everything inside the ZIP's `Connectors\Revit2025\` into `WWP.LandscapeDataManager`.
4. Copy every folder inside the ZIP's `App\` (Dashboard, FloorCalculator, Parameters, …) into `WWP.LandscapeDataManager` as subfolders.
5. Copy `LIMLandscapeData.addin.template` into `%APPDATA%\Autodesk\Revit\Addins\2025\` (next to the folder, not inside it), rename it `LIMLandscapeData.addin`, open it in Notepad and replace `{{ASSEMBLY_PATH}}` with the full path, e.g. `C:\Users\YOUR-NAME\AppData\Roaming\Autodesk\Revit\Addins\2025\WWP.LandscapeDataManager\WWP.LandscapeDataManager.Revit.dll`. Delete any older `WWPLandscapeDataManager.addin` or `WWP.LandscapeDataManager.addin` there.
6. Restart Revit and choose **Always Load** if it asks about the unsigned add-in.

If the company blocks all unsigned programs (AppLocker, Smart App Control, etc.), installing by hand won't get around it — the tools' `.exe` files still won't start. Ask IT to allow `%APPDATA%\Autodesk\Revit\Addins\<year>\WWP.LandscapeDataManager`.

## Build and install

Revit 2025:

```powershell
cd .\LandscapeDataManager
.\Install-Addin.ps1 -RevitVersion 2025
```

Revit 2026:

```powershell
cd .\LandscapeDataManager
.\Install-Addin.ps1 -RevitVersion 2026
```

The script builds the connector against the selected installed Revit API, publishes the self-contained Windows App SDK portion, and installs the files under the current user's Revit add-in directory.

To verify a release build without installing it into Revit, append `-BuildOnly`.

To generate a portable binary ZIP containing the Revit 2025 and 2026 connector DLLs and the WinUI application, run:

```powershell
.\Package-Addin.ps1
```

## Next implementation stage

Before enabling **Apply changes**, validate these rules with a representative Revit model:

1. Whether every environmental value belongs on the instance or type.
2. Whether `Max_Height`/`Max_Width` or `Maxi_Height`/`Maxi_Width` are authoritative.
3. The Revit specs and source units for every numeric parameter.
4. The approved aliases for Revit type names that do not exactly match the source dataset.
5. Whether an apply operation must be atomic or may commit valid types while reporting invalid ones.
