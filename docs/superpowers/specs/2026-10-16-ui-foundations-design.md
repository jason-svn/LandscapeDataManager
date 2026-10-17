# WinUI foundations

Status: Implemented locally on `ui/winui-foundations`. Scope approved in chat on
2026-10-16. Release builds, regression tests and offline Light-theme checks passed;
live Revit, live theme switching and scaled-display interaction remain pending.

## Intent

Improve the existing LIM tools before the next feature phase. Retain their native
WinUI controls, current workflow, existing brand colours in Light mode, and Revit
calculation/write behaviour.

## Scope

- Give all 13 main windows a shared initial sizing policy: dimensions expressed in
  effective pixels, converted using the target window's DPI, bounded by the current
  display work area. Apply after first activation so the first XAML layout receives
  the resize. Preserve Tree Searcher's existing remote-desktop rendering workaround.
- Provide Light, Dark and HighContrast variants for shared status/accent colours and
  Dashboard surfaces. Use system colour pairs in High Contrast and ThemeResource
  references at usage sites. Keep chart categories distinguishable in normal themes.
- Make Floor Calculator's results fill the remaining viewport rather than a fixed
  500-pixel pane inside another vertical scroller. Keep load/calculate commands
  outside the scrollable configuration area, cap configuration to a fraction of the
  available height, and provide horizontal access to wide tables.
- Give Planting Data Sync separate, bounded source, mapping and preview surfaces.
  Keep its title and source status visible, keep Apply's existing eligibility and
  handler, preserve editable column widths, and retain all named controls/bindings.
- Keep Dashboard filters reachable when its window is narrower than the full filter
  row. Keep reporting/export calculations unchanged.

## Validation

- Baseline: 327 existing tests pass on 2026-10-16.
- Build the solution in Release, which does not invoke automatic Debug deployment.
- Run the existing test suite after implementation; validate XAML resources, event
  handlers and named controls. No tests that merely match XAML source strings.
- Inspect running representative tools where desktop automation is available;
  distinguish offline startup checks from live Revit workflow validation.
- Review the complete diff for UI regressions and unintended domain changes.

## Limits

This pass does not redesign navigation across all tools, add features, migrate the
application framework, change calculations, or claim live Revit validation from a
build or standalone startup. Any remaining runtime checks are recorded explicitly.

## Implemented choices

- `Directory.Build.targets` links native helpers only into `UseWinUI` executables.
  Main-window sizing is applied once after activation; Windows owns later resizing.
- Light/Dark/HighContrast resources cover shared status text and Dashboard surfaces,
  category text, charts and metric colours. ThemeResource usage and queued refreshes
  update code-assigned brushes without Revit calls. BNG filter-border refreshes do
  not rebuild its selected rows.
- Use the Windows App SDK desktop `ThemeSettings.CreateForWindowId(...).Changed`
  API alongside `UISettings.ColorValuesChanged`. An initial runtime check caught
  COMException `0x80070490` when subscribing to the UWP
  `AccessibilitySettings.HighContrastChanged`; that subscription was removed.
  [Desktop API reference](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.system.themesettings?view=windows-app-sdk-1.8).
- Planting Data Sync has Map columns, Preview & apply, and Source & pairing tabs.
  The existing header toggle navigates to Source & pairing and reflects tab
  selection. Source configuration cannot consume the preview's height.

## Verification performed on 2026-10-16

- `dotnet build WWP.LandscapeDataManager.sln -c Release --no-restore`: succeeded
  with zero warnings and zero errors. The subsequent final Floor Calculator XAML
  adjustment was also rebuilt separately in Release with zero warnings/errors.
- Existing regression suite: 327 passed, zero failed/skipped.
- All edited XAML parses; `git diff --check` passes. Original XAML names and
  command handlers were retained. No Revit service, calculation or transaction
  implementation was changed.
- Shared status text contrast against the checked surfaces: Light `#FFFFFF`
  4.84–6.19:1; Dark `#2B2B2B` 6.96–8.39:1. These numerical checks do not substitute
  for live High Contrast testing.
- WinApp CLI offline startup and Light-theme screenshots: Importer 1166×793,
  Floor Calculator 1426×853, Dashboard 1386×893, Tree Calculator 1266×833 capture
  pixels. Settings and Tree Searcher also passed the earlier startup smoke check.
  These captures are at the current display configuration, not a DPI matrix.
- Planting Data Sync's preview and source tabs were opened through their observed
  UI Automation selectors; source-tab selection set the header toggle to On.
  Apply stayed disabled without a valid preview. No Apply, calculation, model
  write, settings save, source reload, or export action was invoked.
- Standalone runs used a nonexistent `LIM-UI-Offline-Verification` pipe. Missing
  connection/cancellation status and empty results are expected in these captures.
  All test processes were closed after checks.
- Screenshots: `artifacts/ui-review-2026-10-16/Importer.png`,
  `Importer-preview.png`, `Importer-source.png`, `FloorCalculator.png`,
  `Dashboard.png`, and `ITreeCalculator.png`.
- Independent code review's four important findings were addressed, including
  generated chart brushes, HC category foregrounds and reduced-height source access.

## Remaining runtime checks

- Real Revit model: load populated mappings/floors, preview and apply, verify
  results/selection/column resizing, calculations and exports end to end.
- Live Light/Dark/High Contrast switching, including palette changes and populated
  charts, rows and active filter borders.
- 125%, 150% and 200% displays, small work areas and manual resizing with long
  results, keyboard navigation, and Tree Searcher first-frame rendering over RDP.
