LIM- LANDSCAPE DATA - BINARY DEPLOYMENT

Supported versions: Revit 2025 and Revit 2026
Runtime: .NET 8 with a self-contained WinUI 3 application payload

INSTALL

1. Extract the complete ZIP file.
2. Open PowerShell in the extracted directory.
3. Run one of the following commands:

   .\Deploy-BinaryPackage.ps1 -RevitVersion 2025
   .\Deploy-BinaryPackage.ps1 -RevitVersion 2026

4. Restart Revit.
5. Open EGIS > LIM- LANDSCAPE DATA.

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
