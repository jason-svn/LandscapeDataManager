@echo off
setlocal
rem Double-click launcher for Deploy-BinaryPackage.ps1. Windows blocks unsigned .ps1 files that
rem came from a downloaded ZIP ("is not digitally signed"); -ExecutionPolicy Bypass applies to this
rem one PowerShell process only and changes no system setting. Usage: Install.cmd [2025^|2026]

set "REVIT_VERSION=%~1"
if not defined REVIT_VERSION (
    set /p "REVIT_VERSION=Install for which Revit version? Enter 2025 or 2026: "
)
if not defined REVIT_VERSION (
    echo No Revit version entered.
    goto :done
)

echo.
echo Starting the installer for Revit %REVIT_VERSION%...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Deploy-BinaryPackage.ps1" -RevitVersion %REVIT_VERSION%
if errorlevel 1 (
    echo.
    echo Install failed - see the message above. If scripts are blocked by company policy,
    echo follow "Manual install" in README.txt instead.
)

:done
echo.
pause
