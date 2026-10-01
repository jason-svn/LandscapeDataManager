LIM- LANDSCAPE DATA - BINARY DEPLOYMENT

Supported versions: Revit 2025 and Revit 2026
Runtime: .NET 8 with a self-contained WinUI 3 application payload

INSTALL

1. Extract the complete ZIP file.
2. Close Revit (the installer stops if Revit is open).
3. Double-click Install.cmd and enter 2025 or 2026 when asked.
   (If Windows shows "Windows protected your PC", click More info > Run anyway.)
4. Start Revit and open EGIS > LIM- LANDSCAPE DATA.

Install.cmd runs Deploy-BinaryPackage.ps1 for you. Running the .ps1 directly from PowerShell
fails with "is not digitally signed" on most computers, because Windows blocks unsigned scripts
that came from a download. If you prefer PowerShell, use:

   powershell -ExecutionPolicy Bypass -File .\Deploy-BinaryPackage.ps1 -RevitVersion 2025

UPDATING

From v1.2.0 on, LIM checks for a new release once a day when Revit starts, and
LIM > Project Setup > Check for Updates checks on demand. Choose "Download and install when
Revit closes": a separate window downloads the update while you keep working, then installs it
as soon as you close Revit.

Upgrading from v1.1.0 or older (no Check for Updates button yet): close Revit and run
Install.cmd from this ZIP once. It installs over the old version - nothing needs uninstalling.

MANUAL INSTALL (if scripts are blocked by company policy)

See "Manual install" at https://jason-svn.github.io/LandscapeDataManager/ - it copies the same
files by hand.

DATA SOURCE CONFIGURATION

Open EGIS > LIM- LANDSCAPE DATA, then choose one of these sources:

- Paste a public Airtable base or view share link. CSV downloading must be enabled.
- Select an Excel .xlsx or .xlsm workbook. The first populated row is used for headers.

No Airtable token is required or included in this deployment package. The selected source is remembered per Windows user.

BIODIVERSITY NET GAIN (BNG)

1. Run Import Shared Parameter with the Shared_Parameters_WWP.txt included in this ZIP, so
   the BNG floor parameters are bound. Re-run it after updating: new parameters (baseline
   habitat, Enhanced, ...) only appear once bound.
2. Revit phases decide each floor's role: floors created in Existing are the baseline
   (retained, or lost if demolished in New Construction); tick the floor's
   !_S_PLT_BNGInput_Enhanced_YesNo parameter if a retained habitat is enhanced. Floors
   created in New Construction are new habitat.
3. In Floor Calculator > Biodiversity (BNG), load the floors, fill in the habitats,
   conditions and strategic significance, then Write to Revit.
4. The Dashboard's Site & Biodiversity tab shows baseline vs post-intervention habitat
   units and the net change against the statutory 10%.

ONLINE DASHBOARD

In the Dashboard, click Export JSON, then open
https://jason-svn.github.io/LandscapeCoBenefitsDashboard/ and drop the file in.
Everything runs in your browser; nothing is uploaded.

Quick-start guide: https://jason-svn.github.io/LandscapeDataManager/
