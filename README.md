# LIM- LANDSCAPE DATA

A `.NET 8` Revit 2025+ connector with a separate WinUI 3 interface for replacing the Dynamo-based landscape data workflows.

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
- Compares normalized Revit type names against the selected source using the current Dynamo field conventions.
- Provides a Parameter Mapper that pairs live source columns with writable Revit instance/type parameters.
- Captures a conversion policy for every mapping and can suggest the known WWP environmental mappings.
- Saves mappings to `%LocalAppData%\EGIS\WWP.LandscapeDataManager\parameter-mappings.json`.
- Keeps all Revit operations read-only. Parameter writes are intentionally not enabled yet.

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
