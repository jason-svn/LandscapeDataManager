# WinUI Foundations Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement task by task.

**Goal:** Make the current tool windows scale, theme and scroll consistently.

**Architecture:** Link one native window-sizing helper into each WinUI executable
through a conditional MSBuild item group. Keep shared visual resources in Styles.xaml
and Dashboard-specific resources in that page. Constrain lists with Grid rows and
separate setup from result scrolling using existing native controls.

**Tech Stack:** .NET 8, C# 12, Windows App SDK 1.8, WinUI 3.

**Spec:** ../specs/2026-10-16-ui-foundations-design.md

## Global constraints

- Preserve Revit transactions, handlers, data bindings and export calculations.
- Keep Tree Searcher's remote-desktop workaround.
- Use physical pixels only at AppWindow boundaries and system colour pairs in HC.
- Build Release; do not run the solution's automatic Debug deployment target.

## Review focus

- 125–200% scaling and a work area smaller than the preferred tool dimensions.
- First-frame layout and later user resizing; Tree Searcher over Remote Desktop.
- Dark/High Contrast foreground/background pairings, including category cards.
- Expanded source configuration and long mapping/result lists at reduced height.
- All existing command handlers and Apply's preview-dependent enabled state.

## Task 1: Initial window sizing

- [x] Add SharedUI/WindowSizing.cs with SetInitialSize(Window, int, int).
- [x] Link it only into UseWinUI projects using Directory.Build.targets.
- [x] Replace duplicated initial AppWindow sizing in all 13 MainWindow classes.
- [x] Build all UI projects through the Release solution build.

## Task 2: Theme resources

- [x] Add explicit Light, Dark and HighContrast shared semantic colours.
- [x] Give Dashboard surfaces and category text matching theme resources.
- [x] Replace static references to custom brushes with ThemeResource references.
- [x] Build XAML and inspect representative themes where runtime access permits.
  Light-theme runtime captures completed; Dark/HC switching remains a manual check.

## Task 3: Scrolling and command access

- [x] Give Floor Calculator a constrained, horizontally accessible result viewport.
- [x] Split Importer's setup/mapping/preview into bounded native tab surfaces.
- [x] Make Dashboard's filter row horizontally accessible at smaller widths.
- [x] Build, run existing tests, and check representative startup.
  DPI/resizing matrix remains a manual check; see the design's verification record.

## Task 4: Review and handoff

- [x] Review complete diff and fix confirmed findings.
- [x] Record actual checks and remaining live Revit checks in the design document.
