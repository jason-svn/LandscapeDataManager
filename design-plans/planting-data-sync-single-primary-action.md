# Keep Apply to Revit as Planting Data Sync's only primary action

Written against: `5a18b41771b2ad75a484d1a0d03cf9c71b14ae83`

Status: Implemented. Source checks and Release builds passed; live Revit validation remains outstanding.

All paths below are relative to the LandscapeDataManager repository root. Line numbers describe the recorded commit; recheck the named controls if source has moved.

## Evidence chain

- Surface: the standalone WinUI `WWP.LandscapeDataManager.App.Importer` application, titled **Planting Data Sync**, with **Source & pairing** expanded and a preview containing values ready to apply.
- Problem: **Reload source & Revit catalogs** and **Apply to Revit** both use `LimPrimaryButtonStyle` on the same page (`src/WWP.LandscapeDataManager.App.Importer/MainPage.xaml:74` and `:205`).
- Design evidence: `src/WWP.LandscapeDataManager.SharedUI/Styles.xaml:120` explicitly requires exactly one primary action per screen and plain/secondary styling for other buttons. `LimPrimaryButtonStyle` inherits Fluent's `AccentButtonStyle`; `LimSecondaryButtonStyle` is defined at line 127.
- Runtime ownership: `src/WWP.LandscapeDataManager.App.Importer/WWP.LandscapeDataManager.App.Importer.csproj:30` compiles the shared dictionary as `Styles.xaml`; `src/WWP.LandscapeDataManager.App.Importer/App.xaml:9` merges it into application resources. The two controls resolve their styles from this dictionary.
- State evidence: `src/WWP.LandscapeDataManager.App.Importer/MainPage.xaml.cs:64` only changes the source panel's visibility. The preview enables `ApplyButton` when values are ready at line 415. Neither path swaps button styles or makes the two actions exclusive.
- Owner: the `ConnectButton.Style` reference in `src/WWP.LandscapeDataManager.App.Importer/MainPage.xaml`.
- Scope and affected surfaces: the reload button inside the expanded source panel, in both Airtable and Excel source modes. Mapping and preview/apply remain on the same page.
- Explicit exceptions: None documented.
- Uncertainty: this is established from source. Current rendered appearance, themes, and live Revit operation were not inspected during the audit.

## Design decision

Assign `LimSecondaryButtonStyle` to `ConnectButton`. Keep **Apply to Revit** as the page's sole primary action. This follows the dictionary's explicit rule and the existing treatment of the same reload operation in the page header.

## Reuse

- Reuse `LimSecondaryButtonStyle` from `src/WWP.LandscapeDataManager.SharedUI/Styles.xaml:127` without overriding its setters.
- Exemplar: the header **Reload** button at `src/WWP.LandscapeDataManager.App.Importer/MainPage.xaml:44` already uses this style and the same `Connect_Click` handler.
- No new resource, primitive, variant, or shared-style change is needed.

## Changes

1. `src/WWP.LandscapeDataManager.App.Importer/MainPage.xaml`
   - Change only the style reference on `x:Name="ConnectButton"` from `{StaticResource LimPrimaryButtonStyle}` to `{StaticResource LimSecondaryButtonStyle}`.
   - Preserve its content, name, handler, container, and source-mode visibility. Preserve the existing `ApplyButton` style and enabled-state logic.
   - Verify that the page contains one `LimPrimaryButtonStyle` reference, on `ApplyButton`, and both reload controls use `LimSecondaryButtonStyle`.

## Scope

- Inherit: both source modes receive the button's existing secondary appearance, padding, and corner radius from the shared owner.
- Verify: header reload, expanded-panel reload, preview, and Apply button declarations retain their existing handlers and state settings.
- Exclude: other tool applications, shared dictionary edits, mapping/pairing logic, source loading, transaction behavior, copy changes, new tests, and unrelated local files.

## Validation

- Product: source inspection must confirm that both reload buttons still invoke `Connect_Click`, preview still controls `ApplyButton.IsEnabled`, and only a style reference changed.
- Interface: verify the expanded and collapsed panel declarations and both source-mode branches. When a live visual pass is explicitly requested, inspect expanded and collapsed states before and after a successful preview, with both Airtable and Excel selected. Check that the expanded reload button uses the same secondary treatment as header reload and Apply retains primary treatment. Record the actual viewport and theme inspected; source checks alone do not establish runtime appearance.
- System: reuse `LimSecondaryButtonStyle` directly. Do not introduce local padding, colors, templates, or another button style.
- Repository: run from the repository root:

  ```powershell
  git diff --check
  git diff -- src/WWP.LandscapeDataManager.App.Importer/MainPage.xaml
  rg -n -C 1 'ConnectButton|LimPrimaryButtonStyle' src/WWP.LandscapeDataManager.App.Importer/MainPage.xaml
  ```

  Expected: no whitespace errors; the source diff changes only `ConnectButton.Style`; the remaining primary reference belongs to `ApplyButton`.
- No new automated tests are needed for this single resource-reference change. A build or static check must not be reported as live visual or Revit validation.

Implementation verification: the XAML parsed successfully; both reload buttons retain `Connect_Click` and use `LimSecondaryButtonStyle`; `ApplyButton` is the sole primary button and retains `Apply_Click` and its initial disabled state. `git diff --check` passed, and the product-source diff contains only the planned style-reference replacement.

## Stop conditions

- Recheck this plan if `ConnectButton` has moved, the source panel is now a separate screen, or the primary action rule has changed.
- Stop if a documented exception now permits multiple primary actions on this surface.
- Stop if the correction would require editing the shared dictionary or product behavior; that widens the selected scope.

## Design documentation

- None. The rule already exists in `src/WWP.LandscapeDataManager.SharedUI/Styles.xaml:120`; this change brings one consumer into compliance.
