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
