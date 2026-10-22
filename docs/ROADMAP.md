# LIM Roadmap

What's planned for LIM (Landscape Information Manager), what's done, and the rules changes follow.
Tick items off here as they land, and add the release they shipped in.

## Done

Not released yet (branch `dashboard-include-floors`):

- **Dashboard outlook adds up the growth years.** Trees: each growth-year workset's copies as i-Tree calculated them, with the years in between interpolated (trapezoid rule). No more annual × years, which over-reported with worksets. Planting areas don't grow, so they're the Airtable rate × years. Without growth worksets, trees fall back to annual × years, and the outlook line says so.
- **KPI Schedules group the planting schedule by workset** when growth years exist, with a subtotal per year, so a tree isn't counted once per growth year. (Since Phase 3, running KPI Schedules again adds newer columns to existing schedules; delete one only to change its grouping.)
- Dashboard cards include planting-area (Airtable) carbon, runoff, pollution and cost. BNG labels no longer say "primary" twice.

Shipped in **v1.4.0**:

- **Tree growth driven by the family:**
  - New `!_S_PLT_TreeGrowth_ProjectedYears_Number`; Growth Projections writes the growth year.
  - `AgeAtPlanting` is now per tree, and Import Shared Parameter migrates it from the type.
  - LIM still sets `Years` for families without the formula.
- **Grouped trees are flagged** in Health Check, Growth Projections and Tree Calculator. Trees aren't meant to be grouped, so the growth parameters no longer try to vary by group.
- **Growth Projections warns when a type's DBH never changes** between years.
- **Floor Calculator remembers matches:** on load it restores the landscape match saved in Revit, and a manual pick applies to every floor of that type.

Shipped in **v1.3.0 / v1.3.1**:

- **Importer:** the page opens at Map Columns. Source and instance pairing moved to a "Source & pairing…" panel.
  - Tables have resizable, labelled columns, and the list shows only real changes.
  - Values like `£0.40` import, and `kgCO2e/(m2)/yr`-style headers are read correctly.
- **Floor Calculator:** "Load all floors". Floors store their CO₂e (`iTreeResult_CO2EquivalentAnnual_Mass`).
- **BNG tab:** habitat boxes are searchable dropdowns, and retained floors mirror their baseline in the greyed-out proposed columns.
  - The Family : Type column is frozen.
  - "Highlight selected rows in Revit", and a design-option filter.
- **Connectivity Calculator:** dPC / Probability of Connectivity, bands 1–4, with a hand override via `ConnectivityInput_Locked_YesNo`.
- **Growth Projections:**
  - Year 0 is the Baseline workset, plus linked copies on `LIM_Growth_<N>yr` worksets.
  - Ages are capped at mature size, or at 25 years after planting when a type has no max size.
  - "Create, refresh & calculate" runs i-Tree too.
- **Built-in parameter catalog:** the connector embeds `Shared_Parameters_WWP.txt`, and releases attach it as a backup.
- **Parameters:**
  - Sheet reference rates renamed to `…Rate_Number`.
  - Embodied carbon is calculated (Product + Transport GWP).
  - The Importer can't write to calculated or retired parameters.
- **Dashboard:**
  - "Embodied carbon" with carbon payback.
  - Softscape ratio falls back to the floor type name.

## Tree growth in the family (in progress)

The planting family computes the tree's age; LIM supplies the inputs.

- [x] `!_S_PLT_TreeGrowth_ProjectedYears_Number` (Instance): Growth Projections writes the growth year (0 = Baseline, 5, 10, … from the workset).
- [x] `!_S_PLT_TreeGrowth_AgeAtPlanting_Number` changed from Type to **Instance**, same GUID. Import Shared Parameter moves each type's value onto its trees, and growth copies inherit their original's value.
- [x] Fallback: while a family has no formula yet (its `Years` is still editable), LIM keeps setting `Years` itself, capped as before.
- [x] `WWP_Vegetation_Tree` family (2026-10-21, saved by you):
  - `Mature Age` (family type parameter) = `roundup(MaxHeight / (HeightRatio × 1000 mm))`, worked out from the family's own data. 0 means no max height.
  - `Years` = `AgeAtPlanting + ProjectedYears`, stopped at `Mature Age`. With no max height it's `AgeAtPlanting + min(ProjectedYears, 25)`.
  - `DBH` = `5.4 cm + (Years − AgeAtPlanting) × TrunkDiameterbyYear × 1 cm`, so it grows from the 16/18-girth planting size and stops at maturity.
  - Height and width caps now compare real metres. 16 types had their max size typed in mm; those were converted.
- [ ] **Trunk growth rates:** `!_S_PLT_GrowthRatio_TrunkDiameterbyYear_Number` (cm/yr) is 0 for every type, so DBH stays at 5.4 cm until real rates are entered.
  - [x] **From i-Tree (built 2026-10-21, not run on the sandbox yet):** i-Tree's forecast reports DBH for every year, and grows a trunk by the same amount each year for one species, condition and crown exposure (Acer campestre: 0.44 cm/yr at Good, exposure 3; 0.78 at exposure 5). Growth Projections > Trunk growth from i-Tree rates every planting type with a species code at the condition and exposure most of its planted trees have (Good, 3 when it has none): one forecast per species, condition and exposure.
  - [x] **Write to types** sets the rate on each Revit type (preview, then one undo step).
  - [x] **Push to sheet** saves it in a new "Trunk diameter annual growth rate (cm/yr)" number column of the project's Airtable sheet, adding the column when it's missing (the LIM-DATASET base allows it). The Importer maps that column onto the types.
  - [ ] Run it on the sandbox: Get rates, Write to types, Push to sheet, then Create, refresh & calculate.
- [ ] **Family geometry, not changed:**
  - `TreeOverall_Height = Foliage_Height + TreeTrunk_Diameter` probably should be `+ TreeTrunk_Height`.
  - The drawn trunk (`TreeTrunk_Diameter`, 7.5 cm) isn't linked to DBH.
- [ ] **Other planting families:** give them the same parameters and formulas.

## Phase 2: Design-option scenarios

Every dashboard number depends on picking the right scenario, so this comes before the redesign. Built on branch `roadmap-phases-2-5`, not released yet.

- [x] **Scenario picker:** one option per design-option set, defaulting to each set's primary. A scenario is the main model + the chosen option in every set; elements in options that aren't chosen are left out. The connector lists every set and option, including empty ones (`DesignOptionSetItem`).
- [x] **"Use current view":** works out the scenario from the options visible in the active view (a view collector per option; a set with nothing visible falls back to its primary). Also in Edit… as "Fill from Revit's active view".
- [x] **Canopy cover per scenario:** the union of canopy footprints, overlaps counted once (exact, `CanopyUnion`), ÷ the chosen site area. Trees now carry their plan position; a cached snapshot without positions falls back to summed areas.
- [x] **Greenspace ratio:** pervious ÷ (pervious + impervious) floor area, on Site & Biodiversity and in the scenario overview. Unclassified floors and building slabs (structural floors, "slab"/"suspended"/… types without an LDS type, or tagged `Building`) are listed separately and left out.
- [x] **A "design option" means a scenario:** saved scenarios are named and stored on the project (`ProjectSettingsSnapshot.DesignScenarios`), shown as e.g. "Scheme A = Variante 1: Baseline + Test: Option 1". The Dashboard, BNG tab (as a filter) and JSON export (schema v5, `designOption` + `choices`) all work on the scenario.
- [x] **Scenario overview:** one table with every saved scenario next to the others, plus the difference from a chosen reference scenario (absolute and %; percentage metrics in points). The reference is saved on the project.
- [x] **Performance comparison:** for each scenario, trees, species, canopy cover, greenspace ratio, CO₂e, runoff avoided, pollution removed, oxygen, BNG units/net gain, embodied carbon, ecosystem value, maintenance cost and net value, side by side and as bar charts (lower is better for embodied carbon and maintenance).

## Phase 3: Dashboard redesign

Follows the marked-up screenshot. Built on branch `roadmap-phases-2-5`, not released yet.

- [x] **Ecosystem value band:** split into CO₂ uptake, water runoff and pollution values (trees, i-Tree), plus planting areas (the WWP sheet gives one combined saving), then maintenance and net value.
- [x] **Accumulated amounts:** every benefit card shows kg and m³ over the outlook, not only money; carbon also shows oxygen released.
- [x] **Outlooks:** CO₂e, runoff and pollution outlooks side by side.
- [x] **Project snapshot:** canopy cover (overlaps counted once), water (runoff avoided and rainfall intercepted), number of species and habitat types (BNG habitats, else planting types), planting area and embodied carbon. "Pollution removed" and "Inventory" are gone.
- [x] **Readiness moves to Data Explorer**, next to the status cards. The Dashboard keeps results only.
- [x] **Data Explorer layout:**
  - Units are in the column headers, cells are plain right-aligned numbers (1,234 · 56.7 · 0.42), and columns are wide enough not to clip. Planting-area values follow the Metric/Imperial toggle too.
  - The planting-area table is **under** the tree table, both full width, with oxygen, embodied carbon and upkeep columns.
- [x] **Units per year:** the carbon, water and air-quality cards show the yearly rate now (kg/yr, m³/yr) next to the outlook total.
- [x] **Maintenance cost:**
  - Kept the imported rate's name, `!_S_PLT_LDS_MaintenanceCostAnnual_Currency` (decided 2026-10-21: no rename).
  - Calculated per tree, and per m² × floor area for planting areas, into `!_S_PLT_Result_MaintenanceCostAnnual_Currency`. Tree Calculator (any instance write) and Floor Calculator refresh it; the Dashboard works it out live from the rate.
  - Dashboard: maintenance per year and over the outlook, and **net value** = ecosystem value − maintenance, in the banner and the scenario comparison.
- [x] **Planting-area mix:** planting types by area, and BNG habitat types by area.
- [x] **Top tree palette:** now a per-species table over the outlook — trees, money saved, CO₂e, runoff, pollution and O₂ — sortable by any column, money saved first.
- [x] **Tree oxygen:** Tree Calculator writes `!_S_PLT_Result_OxygenProducedAnnual_Mass` (shared with planting areas) and the new `!_S_PLT_Result_OxygenProducedLifetimeTotal_Mass` = CO₂e × 32/44. Named `Result_` per the naming rules rather than `iTreeResult_`. Trees calculated before this get it from their CO₂e on the Dashboard.
- [x] **Connectivity:** band counts on the Executive overview and Site & Biodiversity, and band/class/score in KPI Schedules (trees and planting areas).
- [x] **Captions:** each card says what it covers — amounts are trees + planting areas, money and rainfall intercepted are trees only.
- [x] **One table language in every tool:** swim-lane column groups with a little white space between lanes, two-line headers (name over unit), plain right-aligned numbers — Health Check, Connectivity, Parameters, Tree Searcher, Growth Projections, Importer, Tree Calculator, iTree Downloader, Sync Audit, Floor Calculator (coefficients and BNG), the Dashboard palette, scenario overview and Original data. Shared styles in `SharedUI/Styles.xaml`; `TableLanes.FollowRows` ends the lanes at the last row. The BNG tab's extra column scroll strip is gone.
- [x] **KPI Schedules is the last button** (Reporting panel, after the Dashboard) and covers the latest parameters: CO₂ equivalent, allergenic, connectivity dPC, and for planting areas CO₂e and BNG (role, baseline habitat/condition/units, proposed habitat, distinctiveness, condition, strategic significance, habitat units, status). Running it again **updates** schedules it made before — missing columns are inserted in place, keeping their filter, grouping, formatting and sheet placement — instead of leaving them unchanged.
- [x] **Tree Searcher remembers manual matches:** a type that already has a species shows it as "Saved match" instead of being re-guessed from its name; a species picked by hand and assigned is remembered per family : type (per user, `species-matches.db` beside the other local LIM data) and suggested as "Remembered match" next time, in any project. Order: saved on the type, then remembered, then the name guess.
- [x] **Importer auto-maps every sheet column it can:** a mapping saved before the `_Rate_Number` renames now follows the rename (CO2 sequestration, runoff, oxygen, product and transport GWP), keeping its on/off and conversion; any other saved mapping whose parameter is gone falls back to the default instead of staying blank. Sync Audit reads saved mappings the same way. New `!_S_PLT_LDS_PollutantsRemovedRate_Number` gives `WWP_Pollutants_Removed` a home like the other rates (needs the next connector install, then Import Shared Parameter); `WWP_Total_GWP` says it's a formula column instead of "Needs mapping", and a column whose parameter isn't bound says to run Import Shared Parameter.
- [x] **The WWP sheet fills what i-Tree doesn't** (Tree Calculator after its i-Tree run, or Floor Calculator with its floors): a tree with no i-Tree result is matched to its sheet row (species code, then type or family name, then the sheet's Generic row) and gets the per-tree CO2e, carbon, oxygen, runoff, pollutants and money saved with status `Estimated` and no input signature, so i-Tree replaces it once the tree can be calculated; trees with i-Tree results are never overwritten. The Dashboard counts estimated trees (and says how many), with an Estimated status card. Every matched tree and planting area also gets pollen (new `!_S_PLT_Result_PollenAnnual_Mass`), and Site & Biodiversity shows pollen load, allergenic trees and irrigation demand. Fixed on the way: the sheet's money is text in pounds ("£15.80"), which Floor Calculator never parsed, so every planting area's cost saved was 0 (it now converts pounds to the project currency, and re-syncs an old cache on open); i-Tree results also store their pollutant total in `!_S_PLT_Result_PollutantsRemovedAnnual_Mass`.
- [x] **Matching from the WWP sheet too.** Tree Searcher: after a saved or remembered match, a type the sheet lists by name (its Types column, e.g. Acer_Campestre) takes the sheet's species code, or, when the row has no code, the i-Tree species with that scientific name; only then the name guess against i-Tree's catalogue (it scores the family name first, so a generic family could match the wrong species). The sheet gives 21 of its 45 tree rows a code; the rest are found by scientific name. Floor Calculator: a sheet row named exactly like the Revit floor type ("Planting type" or "Types") wins, then the same name without WWP/Subs/Surface(s)/Vegetation prefixes when only one row has it (WWP_Subs_Raingarden, WWP_Surfaces_Asphalt), then the fuzzy match.
- [x] **Stale trees are flagged on the Dashboard.** It used to read the stored status, so a tree whose inputs changed after i-Tree calculated it still read Calculated (the sandbox had 520 of them). The report now carries each tree's i-Tree inputs and the Dashboard works out the status as Tree Calculator does: Stale trees stay in the totals with their old i-Tree results, under a red banner saying how many and how to recalculate (Tree Calculator > Validate > Calculate valid), with a button to list them in Data explorer. Trees with no result at all get an amber banner. (Connector change.)
- [x] **Maintenance cost card:** a fourth category card beside carbon, storm water and air pollution: maintenance over the outlook, trees and planting areas, net value, and the yearly rate today.
- [x] **JSON export and the web dashboard (2026-10-21):** the export summed each growth year's copies, so with trunk growth at 0 the web dashboard showed the same benefit in every year. It now uses each planted tree's i-Tree forecast for the year, as the Dashboard does (9,323 to 34,524 kg CO2e from year 0 to 25 on the cached sandbox snapshot). Schema 6 adds stale and sheet-estimate tree counts, pollen, allergen and irrigation figures and the native share's denominator; nothing earlier changed. The web dashboard (LandscapeCoBenefitsDashboard, committed locally, not pushed) leads with a maintenance cost and net value panel, shows notices for stale, missing and estimated trees, and adds pollen, allergenic and low-water tiles. Checked in headless Edge: the schema 6 export and the bundled v3 sample load with no script errors, NaN or undefined values in any growth year or period.

## Phase 4: i-Tree timeline instead of growth copies

Every growth year is still modelled as copies on its `LIM_Growth_<N>yr` workset for drawings, but the numbers now come from i-Tree's own forecast. Built on branch `roadmap-phases-2-5`, not released yet.

- [x] **Why:** an outlook is an accumulated value (year 1 + year 2 + … + year N). Annual × years under-reports a growing tree, and a year-N copy's annual × N over-reports it.
- [x] **i-Tree year-by-year forecast (run once, project without copies):**
  - Tree Calculator and Growth Projections' "i-Tree by growth year" both run it after the usual calculation (`ITreeTimelineCalculator`, shared through `ITreeBatchCalculator`).
  - One request per **unique planting input**: species, planting DBH, condition, crown exposure, location and horizon (`TreeTimelines.Key`). Identical trees share it. Live check 2026-10-21: Acer campestre, 25 years, 2.8 s.
  - **Horizon: the family's `Mature Age` − `AgeAtPlanting`, at most 35 years**; 25 when the family has no mature age (its own cap). After the horizon the benefit holds at its mature value.
  - Stored once per input in the project (extensible storage on one DataStorage element, vendor EGIS: `TreeTimelineStore`). Each planted tree carries `!_S_PLT_Result_iTreeTimelineKey_Text` and its 5/10/20/25-year totals: `Result_CO2Equivalent{N}yr_Mass`, `Result_RunoffAvoided{N}yr_Volume`, `Result_PollutantsRemoved{N}yr_Mass`, `Result_CostSaved{N}yr_Currency` (also in KPI Schedules).
  - The Dashboard and the scenario comparison read any outlook year from the timeline (`TreeOutlook`), leaving growth copies out of the numbers; growth worksets and annual × years remain the fallbacks, and the outlook line says which one is in use.
  - Re-run only when an input changes: a stored key is never requested again.
  - Source of truth: **i-Tree's timeline for benefits, the family for geometry** (the recommended choice).
  - Floors don't need this: their benefits are Airtable rate × area, constant every year.
- [x] **Lifetime parameters:** for planted trees, `…LifetimeTotal` is now the whole forecast (planting to maturity), the same span on every tree. Growth-year copies keep their own per-year calculation.
- [ ] **Check the family data:** two Prunus avium types report `Mature Age` = 1, below their planting age of 5, so their forecast is one year long. Probably a units slip in their max height. The sheet is right (checked 2026-10-21: Prunus_Avium and Prunus_Avium_Plena, max height 20 m, height growth 0.6 m/yr, so `Mature Age` should be 34); still to check: the two types' max height and height ratio in Revit.

## Phase 5: Live Revit testing

Model: "LAN_LIM Arboris_EWR Sandbox" (workshared, ACC). Writes are fine for testing, but **never Sync with Central**.

Checked by script on 2026-10-21, with Revit still running the v1.4 connector:

- [x] Design option sets read from Revit, including options with nothing in them yet (Variante 1: Baseline / Option 1 / Option 2; Test: Option 1 / Option 2).
- [x] A view collector with `ElementDesignOptionFilter` returns only the options the active view shows, which is how "Use current view" works out the scenario.
- [x] The new Dashboard runs against the live model through the old connector (older reports still load): scenario picker, Edit… dialog, scenario overview, redesigned overview and Data explorer.
- [x] Revit parses the updated `Shared_Parameters_WWP.txt` (160 definitions; the new ones are Mass/Volume/Currency/Text as intended).
- [x] `HOST_AREA_COMPUTED` matches a floor's Area, so maintenance per m² × area uses the right area.
- [x] Live i-Tree forecast for one planting input: 25 years in 2.8 s, parsed year by year.

Run live on 2026-10-21 with the new build installed (connector driven over its pipe, plus the Dashboard and Floor Calculator UIs). Test data added to the sandbox, **not saved or synced**: Variante 1 Option 1 = Baseline's 541 trees with every other one a Quercus robur; Variante 1 Option 2 = every third Baseline tree (181); Test Option 2 = Test Option 1's 541 trees, a third Betula, no planting areas; four saved scenarios (Scheme A–D); a "LIM test - scenario view" 3D view.

- [x] **Import Shared Parameter:** the 19 new parameters bound ("Created: 19"), no conflicts.
- [x] **Tree Calculator:** 13 unique i-Tree inputs for 2,949 trees; the forecast covered 10 unique planting inputs (horizons 1–35 years), written to every planted tree; growth copies got no key. A second run requested nothing ("already stored"). Values match the stand-alone forecast (Acer campestre 44.7 kg CO₂e over 10 years, 165.8 kg over 25; lifetime = the 25-year forecast).
- [x] **Maintenance:** trees store £20/yr; floors store rate × area (e.g. wetland £8 × 20,406 m²), matching the Dashboard's live total (£3.64M/yr; the sandbox floors are site-sized). Tested through the instance writer; Floor Calculator's own run writes the same way.
- [x] **Dashboard:** every option listed, including empty ones; "Use current view" returned Variante 1 = Option 1 in a view showing it; four saved scenarios compared against Scheme A (table and charts); the outlook line reads "i-Tree's year-by-year forecast"; JSON export is schema 5 with 4 schemes × 4 growth years, `designOption` and `choices`.
- [x] **BNG tab:** the scenario filter lists the saved schemes; Scheme A +13.2%, Scheme D −24.4%, the same as the Dashboard.
- [x] **KPI Schedules:** rebuilt (9, including the new options) with the connectivity, oxygen, maintenance and forecast columns.
- [x] **Connectivity:** band counts show on the Dashboard (312 critical, 537 strong, 453 moderate, 321 low, 43 not assessed).
- [x] **Growth Projections:** for a design option that isn't active, the run moves the originals to the Baseline workset and skips the copies with a note to activate the option; with it active, 543 copies were created in that option on the right worksets (ProjectedYears 10 → Years 15); a rerun updated the 543 and created none.
- Fixed while testing: Floor Calculator crashed on launch (an incomplete `.pri` from an incremental build — a clean rebuild fixed it; it now logs startup crashes to `%TEMP%`); "Raingarden" and wetland types now count as pervious; `WWP_Surfaces_Building` floors (matched to Asphalt) are left out as buildings; KPI schedule and Growth Projections labels no longer say "primary" twice (connector change: needs the next install, then rebuild KPI schedules).
- [ ] **Excel Importer** wasn't run (maintenance rates were already on the types).
- [ ] Check the mature-size measurement against the real planting families (see the Prunus avium `Mature Age` = 1 note in Phase 4).

## Follow-ups

- [ ] **Growth Projections — "Remove years no longer listed":** deletes those years' copies, then their empty worksets.
- [ ] **Growth Projections — wrong-option copies:** a copy in a different design option from its original is deleted and recreated in the right option, when that option is active.
- [ ] **Refresh and Audit:** after it applies new sheet rates, recalculate the affected floors automatically, so floor totals never go stale.
- [ ] **Importer:** optionally copy values to the new parameter on a rename instead of recalculating. Current choice: recalculate.

## Data issues in the WWP landscape data sheet (Airtable)

- [ ] **`WWP_Vegetation_Shrub` avoided runoff:** 0.83 looks like per-shrub, not per m² (other vegetation is 0.04–0.49). Fixed in Airtable 2026-10-16; confirm.
- [ ] **`f_int and Crg…` runoff coefficient:** Meadow was `7%`, should be `0.07`. Fixed in Airtable 2026-10-16; confirm.
- [ ] **Cost columns:** `COST SAVED per year …` hold ranges (`£20-60`) that can't import as numbers. LIM now reads the sheet's money ("£15.80") and takes a range's midpoint for the cached sheet, but the per-benefit split still doesn't add up to `COST_SAVED`.
- [ ] **`WWP_Total_GWP`:** a plain number column in the "Imported table" (checked 2026-10-21 via the Airtable schema), not a formula, and Product/Transport GWP are empty. Its values follow embodied − yearly CO₂ sequestration: Asphalt 18 − 0 = 18, Acer campestre 0 − 28 = −28. LIM uses only the positive (embodied) part.

## Rules for changes

### Parameter names

- **Calculated values:** start with `!_S_PLT_Result_…`. Exceptions kept for existing projects: `!_S_PLT_iTreeResult_*`, `!_S_PLT_BNGResult_*` and `!_S_PLT_ConnectivityResult_*`.
- **Sheet reference rates:** per tree or per m², on the Type, named `…Rate_Number`, never `_Mass`/`_Volume`.
- **Sheet formula columns:** get no parameter; LIM calculates them.

### Renaming or retyping a parameter

- Give it a **new GUID**, add the old name to `legacyAliases` in `SharedParameterSetupService`, and remove the old row from `Shared_Parameters_WWP.txt`.
- **Never** reuse a GUID under a new name, and never give an existing name a new GUID. Projects identify parameters by GUID, so either one breaks them.
- If a name returns to an earlier one, reuse the GUID it originally shipped with.
- Safety net: `ResultParameterReusePolicy` lets Import Shared Parameter keep an existing BNG/Connectivity *result* parameter whose name and type match but whose GUID differs. It's for projects bound from older catalogs, not a substitute for the rules above.

### Shared parameter file

- `Shared_Parameters_WWP.txt` is UTF-16 LE with BOM and CRLF line endings. Edit it with a script, never by hand in an editor.
- It's embedded into the connector at build time, so update it before running `Package-Addin.ps1`.
