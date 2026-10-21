# LIM Roadmap

What's planned for LIM (Landscape Information Manager), what's done, and the rules changes follow.
Tick items off here as they land, and add the release they shipped in.

## Done

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
- [ ] **Trunk growth rates:** `!_S_PLT_GrowthRatio_TrunkDiameterbyYear_Number` (cm/yr) is 0 for every type, so DBH stays at 5.4 cm until real rates are entered. There's no column for it in the Airtable sheet yet.
- [ ] **Family geometry, not changed:**
  - `TreeOverall_Height = Foliage_Height + TreeTrunk_Diameter` probably should be `+ TreeTrunk_Height`.
  - The drawn trunk (`TreeTrunk_Diameter`, 7.5 cm) isn't linked to DBH.
- [ ] **Other planting families:** give them the same parameters and formulas.

## Phase 2: Design-option scenarios

Every dashboard number depends on picking the right scenario, so this comes before the redesign.

- [ ] **Scenario picker:** one option per design-option set, defaulting to each set's primary. Today, picking an option in one set drops every other set, even its primary.
- [ ] **"Use current view":** works out the scenario from the options visible in the active view.
- [ ] **Canopy cover per scenario:** the union of canopy footprints (overlaps counted once) ÷ the chosen site area.
- [ ] **Greenspace ratio:** pervious ÷ (pervious + impervious) floor area. Unclassified floors are listed separately, and building slabs are left out.

## Phase 3: Dashboard redesign

Follows the marked-up screenshot.

- [ ] **Ecosystem value band:** split into CO₂ uptake, water runoff and pollution values.
- [ ] **Accumulated amounts:** every benefit card shows kg and m³ over the outlook, not only money.
- [ ] **Outlooks:** add pollution and runoff outlooks next to the carbon outlook.
- [ ] **Project snapshot:** canopy cover, plus the number of species and habitat types. Remove "Pollution removed" and "Inventory".
- [ ] **Planting-area mix:** fill it with the species and habitat-type lists.
- [ ] **Top tree palette:** replace it with a per-species table: count, money saved, CO₂, runoff, pollution and O₂ per row, sortable, defaulting to money saved.
- [ ] **Tree oxygen:** add `iTreeResult_OxygenProducedAnnual/LifetimeTotal_Mass` = CO₂e × 32/44 (≈ 0.727), or i-Tree's "Carbon Sequestered" (kg of carbon) × 2.67.
- [ ] **i-Tree year-by-year forecast:** store it (or its 5/10/20/25-year totals) on each tree, so outlooks stop using annual × years. That shortcut under-reports growing trees.
- [ ] **Connectivity:** band counts on the dashboard, plus the connectivity fields in KPI Schedules.
- [ ] **Captions:** fix the cards that say "trees + planting areas" but count trees only (ecosystem value, CO₂ equivalent, runoff).

## Phase 4: Live Revit testing

- [ ] Run every tool against a real workshared project: Import Shared Parameter, Excel Importer, Floor Calculator + BNG, Connectivity, Growth Projections (incl. i-Tree), Dashboard and KPI Schedules.
- [ ] Growth Projections: check copies in a design option that **isn't** active, and that existing copies refresh correctly.
- [ ] Check the mature-size measurement against the real planting families.

## Follow-ups

- [ ] **Growth Projections — "Remove years no longer listed":** deletes those years' copies, then their empty worksets.
- [ ] **Growth Projections — wrong-option copies:** a copy in a different design option from its original is deleted and recreated in the right option, when that option is active.
- [ ] **Refresh and Audit:** after it applies new sheet rates, recalculate the affected floors automatically, so floor totals never go stale.
- [ ] **Importer:** optionally copy values to the new parameter on a rename instead of recalculating. Current choice: recalculate.

## Data issues in the WWP landscape data sheet (Airtable)

- [ ] **`WWP_Vegetation_Shrub` avoided runoff:** 0.83 looks like per-shrub, not per m² (other vegetation is 0.04–0.49). Fixed in Airtable 2026-10-16; confirm.
- [ ] **`f_int and Crg…` runoff coefficient:** Meadow was `7%`, should be `0.07`. Fixed in Airtable 2026-10-16; confirm.
- [ ] **Cost columns:** `COST SAVED per year …` hold ranges (`£20-60`) that can't import as numbers.
- [ ] **`WWP_Total_GWP`:** a formula column. It's −(CO₂ sequestration) for planting, which isn't embodied carbon. LIM ignores it and calculates embodied GWP itself.

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
