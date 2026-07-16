WWP LANDSCAPE DATA MANAGER - BINARY DEPLOYMENT

Supported versions: Revit 2025 and Revit 2026
Runtime: .NET 8 with a self-contained WinUI 3 application payload

INSTALL

1. Extract the complete ZIP file.
2. Open PowerShell in the extracted directory.
3. Run one of the following commands:

   .\Deploy-BinaryPackage.ps1 -RevitVersion 2025
   .\Deploy-BinaryPackage.ps1 -RevitVersion 2026

4. Restart Revit.
5. Open EGIS > Landscape Data.

AIRTABLE CONFIGURATION

Set these Windows user environment variables before starting Revit:

WWP_AIRTABLE_TOKEN
WWP_AIRTABLE_BASE_ID

The token is not included in this deployment package.
