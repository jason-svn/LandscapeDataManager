LIM- LANDSCAPE DATA - BINARY DEPLOYMENT

Supported versions: Revit 2025 and Revit 2026
Runtime: .NET 8 with a self-contained WinUI 3 application payload

INSTALL

1. Extract the complete ZIP file.
2. Close Revit (the installer stops if Revit is open).
3. Double-click Install.cmd and enter 2025 or 2026 when asked.
   (If Windows shows "Windows protected your PC", click More info > Run anyway.)
   The window lists each step as it copies the tools and says "Done" when finished.
4. Start Revit and open the LIM tab.

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

Open the LIM tab in Revit, then choose one of these sources:

- Paste a public Airtable base or view share link. CSV downloading must be enabled.
- Select an Excel .xlsx or .xlsm workbook. The first populated row is used for headers.

No Airtable token is required or included in this deployment package. The selected source is remembered per Windows user.

SHARING SETTINGS

In LIM > Settings, Export settings... saves the project's units, currency, data sources,
shared parameter file path and Excel Importer mappings to a .limsettings file. Send it to a
colleague (or use it on another project): Import settings... lets them tick which parts to
apply. Keys (i-Tree API key, Airtable token) are only included if you tick that option when
exporting - anyone with the file can then use them.

GROWTH YEARS (WORKSETS)

Model each growth year of the trees on its own workset with the year in its name, e.g.
"Trees - 5 Years", "Trees - 10 Years". Trees on any other workset (e.g. existing trees),
floors and lighting count in every year. The Dashboard's Growth year slider shows one year
at a time. Use design options for real alternatives (e.g. Scheme A / Scheme B): Export JSON
then writes every scheme at every growth year, and the online dashboard shows a Scenario
dropdown next to its growth-year slider. The design option whose name contains "Baseline"
opens by default, in the Dashboard and online.

If nursery stock is already some years old when planted, enter that on the planting type in
!_S_PLT_TreeGrowth_AgeAtPlanting_Number. When Tree Calculator loads, it sets each tree's
!_S_PLT_TreeGrowth_Years_Number to age at planting + its workset's year (e.g. 5 + 10 = 15),
so the family sizes the tree, and i-Tree calculates it, at its real age. Run Import Shared
Parameter after updating to add the new parameter.

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
   Floors of the same type and role share their inputs: fill one and the others follow,
   and empty floors take the inputs of a same-type floor when loaded. Tick the box in front
   of a floor's name (Unique) to give it its own inputs. Unique needs the
   !_S_PLT_BNGInput_Unique_YesNo parameter, so run Import Shared Parameter again after updating.
4. The Dashboard's Site & Biodiversity tab shows baseline vs post-intervention habitat
   units and the net change against the statutory 10%.

ONLINE DASHBOARD

In the Dashboard, click Export JSON, then open
https://jason-svn.github.io/LandscapeCoBenefitsDashboard/ and drop the file in.
Everything runs in your browser; nothing is uploaded.

Quick-start guide: https://jason-svn.github.io/LandscapeDataManager/
