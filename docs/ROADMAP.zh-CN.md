# LIM 路线图（中文版）

> 本文是 [`ROADMAP.md`](ROADMAP.md) 的中文翻译，内容截至 2026-10-22。两者不一致时以英文版为准。
> 参数名、工具名、按钮名、文件名和分支名保留英文，和 Revit 及各工具里显示的一致。

LIM（Landscape Information Manager）的计划、已完成的工作，以及修改时要遵守的规则。
每完成一项就在这里打勾，并注明发布的版本。

## 已完成

尚未发布（分支 `dashboard-include-floors`）：

- **Dashboard 的展望期按生长年份累加。** 树：用每个生长年份 workset 上副本的 i-Tree 结果，中间年份插值（梯形法）。不再用"年值 × 年数"，那样在有 workset 时会多算。种植区不会生长，所以是 Airtable 费率 × 年数。没有生长年份 workset 时，树退回"年值 × 年数"，展望期说明里会写明。
- **KPI Schedules 在有生长年份时按 workset 给种植明细表分组**，每年一个小计，这样一棵树不会每个生长年份都被计一次。（从 Phase 3 起，再次运行 KPI Schedules 会把新增的列加到已有明细表里；只有要改分组时才需要删掉明细表。）
- Dashboard 卡片计入种植区（Airtable）的碳、径流、污染物和节省费用。BNG 标签不再重复显示两次 "primary"。

在 **v1.4.0** 中发布：

- **树的生长由族驱动：**
  - 新参数 `!_S_PLT_TreeGrowth_ProjectedYears_Number`，Growth Projections 写入生长年份。
  - `AgeAtPlanting` 改为每棵树（实例）各自的值，Import Shared Parameter 会把它从类型迁移过来。
  - 对还没有公式的族，LIM 仍然直接设置 `Years`。
- **成组（group）的树会被标出**，在 Health Check、Growth Projections 和 Tree Calculator 里。树本来就不该成组，所以生长参数不再尝试按组变化。
- **Growth Projections 会提示某个类型的 DBH 在各年份之间没有变化。**
- **Floor Calculator 记住匹配结果：** 打开时恢复保存在 Revit 里的景观匹配，手动选择会应用到该类型的所有 floor。

在 **v1.3.0 / v1.3.1** 中发布：

- **Importer：** 页面直接打开到 Map Columns。数据源和实例配对移到 "Source & pairing…" 面板。
  - 表格的列可以调宽、带标签，列表只显示真正有变化的项。
  - 像 `£0.40` 这样的值可以导入，`kgCO2e/(m2)/yr` 这类表头也能正确识别。
- **Floor Calculator：** 新增 "Load all floors"。floor 会保存自己的 CO₂e（`iTreeResult_CO2EquivalentAnnual_Mass`）。
- **BNG 页：** 生境下拉框可以搜索；保留的 floor 在灰色的拟建列里显示与基线相同的内容。
  - Family : Type 列冻结。
  - "Highlight selected rows in Revit"，以及 design option 筛选。
- **Connectivity Calculator：** dPC / 连通概率（Probability of Connectivity），分 1–4 档，可通过 `ConnectivityInput_Locked_YesNo` 手动覆盖。
- **Growth Projections：**
  - 0 年是 Baseline workset，另外在 `LIM_Growth_<N>yr` workset 上生成带连接的副本。
  - 年龄上限为成熟尺寸；类型没有最大尺寸时，上限为种植后 25 年。
  - "Create, refresh & calculate" 同时运行 i-Tree。
- **内置参数目录：** Revit 端插件内嵌 `Shared_Parameters_WWP.txt`，发布版本也附带一份作为备份。
- **参数：**
  - 表格里的参考费率改名为 `…Rate_Number`。
  - 隐含碳会计算出来（Product + Transport GWP）。
  - Importer 不能写入计算类参数或已退役的参数。
- **Dashboard：**
  - "Embodied carbon"（隐含碳）及碳回收期。
  - 软质比例在没有数据时退回使用 floor 类型名判断。

## 族里的树木生长（进行中）

种植族负责计算树的年龄，LIM 提供输入。

- [x] `!_S_PLT_TreeGrowth_ProjectedYears_Number`（实例参数）：Growth Projections 写入生长年份（0 = Baseline，5、10……来自 workset）。
- [x] **由预测年份决定 workset（2026-10-22，你的决定）：** 树的 `ProjectedYears` 决定它属于哪个生长年份 workset。自动年龄更新只给空着的 `ProjectedYears` 按 workset 补值，不再覆盖你填的值；年份对不上的树只计数（"Sort & check trees 会移动它们"）。Create & refresh 会把预测年份大于 0 的手放树当作生长年份的树，不再当作要复制或移回 Baseline 的原始树。
- [x] **副本带标注并锁定（2026-10-22）：** Growth Projections 在它生成或刷新的每个副本上写入 `!_S_PLT_TreeGrowth_GeneratedBy_Text`（新参数，实例，Identity Data 组），例如 "LIM Growth Projections: 10-year copy of tree 666206. Edit the original tree, not this copy."，并把副本锁定（Pin）。刷新或删除副本时会先解锁。需要先运行 Import Shared Parameter；沙盒里已有的 63 个副本会在下次运行时补上。
- [x] **0 年是项目开始（2026-10-22）：** 0 年 workset 改名为 "LIM_Growth_0yr - Project start"，不再叫 "- Baseline"：Baseline 指的是 Existing 阶段（BNG）。旧名字仍然识别为 0 年。
- [x] **新树按设计年份画成长大后的样子（2026-10-22，你的决定）：** 不是 Growth Projections 副本的树都是原始树，画在它自己的年份：`ProjectedYears` 大于 0 时用它，否则按阶段决定——现状树按今天的样子画在 0 年，新建树画在设计年份（Growth Projections > "New trees drawn at"，默认 25，保存在项目里）。只为你列出的年份生成副本，不会为原始树自己那一年生成（遗留的会被删除），所以新树没有 0 年副本。Sort & check trees 和 Create & refresh 会把每棵原始树放到它那一年的 workset，并写入年份。新建树画成长大后的样子时，i-Tree 预测从种植时的 DBH（5.4 cm）开始；现状树从今天的 DBH 开始，所以每年的效益与树画在哪一年无关。树现在带有"是否新建"和"是否副本"两项信息；Dashboard 的预测按原始树统计，而不是按 0 年的树。
- [x] **建模约定（2026-10-22 确定）：** 主模型放现状场地（第一个阶段）；design option 放各个方案。现状保留：创建于 Existing，不拆除。现状损失：创建于 Existing，拆除于 New Construction（把现状 floor 切开，让拆除的部分正好位于新建部分下方）。提升：保留，并勾选 `!_S_PLT_BNGInput_Enhanced_YesNo`。新建：创建于 New Construction。同一阶段内 floor 不互相重叠。Revit 不能只在某一个 option 里拆除主模型的元素，所以各方案处理方式不同的现状部分要放进该 set 的每一个 option 里。现状树：AgeAtPlanting = 今天的树龄，年份为 0。
- [x] **Dashboard 按阶段统计（2026-10-22，schema 7）：** 方案的效益、面积和维护成本只计算建成后还在的元素（拆除的树和种植区不计入）；BNG 仍然用损失的 floor 计算基线。新增 "Existing site" 方案（第一个阶段存在的元素），作为方案对比的默认参照，所以每个方案都与现状场地对比；它的 BNG 就是基线，没有变化。JSON 导出用 `isExistingSite` 标记它。树从 Revit 端插件带上阶段角色（floor 之前已经通过 BNG 带上了）。
- [x] **沙盒重新建模（2026-10-22，用脚本完成，未同步，每一步都可单独撤销）：**
  - 2,139 棵树从 Workset1 移到 0 年 workset。
  - 删除 41 块重复的 floor（Test : Option 1 和 Variante 1 : Baseline 里被粘贴了 2–3 次的复制品），以及 Test : Option 1 里一块叠在新版 Meadow 和 Shrub 上的旧 Meadow（78,248 m²）。
  - 主模型只放现状：删除了主模型里的 10 块新建 floor、540 棵新建树及其 42 个生长副本；删除了各 option 里复制来的 5 块现状 floor。
  - 方案按地理位置在 x = 187.8 m 处拆分（两边面积相等）：Variante 1 保留西边，Test 保留东边。"Variante 1 : Baseline" 改名为 "Option A"。刻意做出的差异：Variante 1 的 Option A = 地面 + 239 棵树，Option 1 = 只有树，Option 2 = 80 棵树；Test 的 Option 1 = 地面 + 301 棵树，Option 2 = 草甸和灌木改为草坪、树减半（原来手工做的 25 年树改为 0 年）；Empty option 保持为空。
  - 现状 Meadow（253,433 m²）切成 20 块拆除 floor（247,262 m²，位于方案下方）和 15 块保留 floor（6,154 m²），每块都沿用原 floor 的类型、标高、workset 和 47 个参数。其他四块现状 floor 在方案范围外，保持不变。
  - 还需要在 Revit 里做：Floor Calculator（面积变了；Test : Option 2 改成了草坪）、为改了年份的树运行 Tree Calculator，以及 Growth Projections。
- [x] **沙盒清理（2026-10-22，用脚本完成，未同步）：** 删除了 546 棵重叠的重复树（同一 workset、design option 和类型，距离 0.1 m 以内），其中 542 棵在 "Variante 1 : Baseline"，这个 option 被粘贴了两次；一步撤销。树从 3,490 棵减到 2,944 棵，每个 design option 540 棵（Variante 1 : Option 2 为 181 棵）。"Test : Option 2" 是放在 LIM_Growth_25yr 上的一套手工 25 年布置（540 棵树）：是否保留还待决定。
- [ ] **Growth Projections > Sort & check trees（2026-10-22 完成开发，尚未在沙盒里运行）：**
  - **Sort into worksets：** 把每棵树移到它预测年份的 workset（Growth Projections 副本移到它连接的年份），缺少的 workset 会新建；一步撤销。成组的树和年份不在 0–100 之间的树会单独列出，不移动。
  - **重叠的树：** 同一 workset、同一 design option 内，距离小于设定值（默认 0.1 m）、且类型相同（可取消勾选）的树。每一处保留一棵（优先保留有副本的原始树，其次是工具生成的副本，再次是最早放置的那棵），其余的在当前视图中标红，提供 Select、Reset colours 和 "Delete the red trees"（一步撤销，连同由它们生成的生长年份副本一起删除）。
- [x] `!_S_PLT_TreeGrowth_AgeAtPlanting_Number` 从类型参数改为**实例参数**，GUID 不变。Import Shared Parameter 会把每个类型的值移到它的各棵树上，生长副本继承原树的值。
- [x] 兜底：族还没有公式时（它的 `Years` 仍可编辑），LIM 继续自己设置 `Years`，上限规则同前。
- [x] `WWP_Vegetation_Tree` 族（2026-10-21，由你保存）：
  - `Mature Age`（族类型参数）= `roundup(MaxHeight / (HeightRatio × 1000 mm))`，由族自身的数据计算。0 表示没有最大高度。
  - `Years` = `AgeAtPlanting + ProjectedYears`，到 `Mature Age` 为止。没有最大高度时为 `AgeAtPlanting + min(ProjectedYears, 25)`。
  - `DBH` = `5.4 cm + (Years − AgeAtPlanting) × TrunkDiameterbyYear × 1 cm`，从 16/18 胸围的种植规格开始生长，到成熟为止。
  - 高度和宽度上限现在按真实米数比较。有 16 个类型的最大尺寸是按 mm 输入的，已经换算。
- [ ] **从 i-Tree 取生长率：** 族用 `!_S_PLT_GrowthRatio_TrunkDiameterbyYear_Number`（cm/年）增长 DBH，用 `..._HeightbyYear_Number` / `..._WidthbyYear_Number`（m/年）增长树高和冠宽，直到 i-Tree 给出这棵树自己的尺寸（见下）。
  - [x] **树干（2026-10-21 完成开发）：** i-Tree 的预测会给出每一年的 DBH，而且对同一树种、树况和冠层光照暴露度，每年树干增长的量相同（Acer campestre：Good、暴露度 3 时为 0.44 cm/年；暴露度 5 时为 0.78），所以斜率就是族要的生长率。每个有树种代码的种植类型都按该类型多数树的树况和暴露度取值（没有树时用 Good、3）：每种树种、树况和暴露度的组合只请求一次预测。
  - [x] **树高和冠宽（2026-10-22，你的决定：用 i-Tree 覆盖表格里的值）：** i-Tree 的预测也给出每一年的树高和树冠面积。它们从种植时的尺寸开始，而且越长越慢（Quercus robur，Good，3：种下时 6.34 m 高，第 1 年 +0.41 m，第 25 年 +0.30 m），所以从 0 开始的单一速率不可能每年都对上：速率取设计年时 i-Tree 的尺寸除以设计年（Quercus robur：25 年后 15.21 m 高、冠宽 8.04 m，即 0.61 和 0.32 m/年）。这样在设计年画出来的树，即使还没计算，大小也和 i-Tree 一致。树高速率也决定 `Mature Age`。
  - [x] Growth Projections 第 3 步改为 **Growth rates**（树干、树高、冠宽）。**Write to types** 把三个速率都写到每个 Revit 类型上（先预览，一步撤销）。**Push to sheet** 覆盖 Airtable 表格里的 "Trunk diameter annual growth rate (cm/yr)"、"Height year annual growth rate (m/yr)" 和 "Width annual growth rate (m/yr)" 三列，缺哪列就加哪列；Importer 会把三列都映射到类型参数上。
  - [ ] 在沙盒里运行：Get rates、Write to types、Push to sheet，然后 Create, refresh & calculate。
- [x] **按 i-Tree 的尺寸画树（2026-10-22，你的决定）：** i-Tree 根据 DBH 推算树高和树冠（LIM 不发送树高和树冠），所以画出来的树现在用 i-Tree 的数字。新增两个实例参数（Analysis Results）：`!_S_PLT_iTreeResult_TreeHeight_Length` 和 `!_S_PLT_iTreeResult_CrownWidth_Length`（与 i-Tree 树冠面积相等的圆的直径）。每次 i-Tree 计算（Tree Calculator、Growth Projections、Refresh and Audit）都按树的 DBH 写入；i-Tree 算不了时写 0。族用它们画：树干高 = 树高 / 3，树冠高 = 2 × 树高 / 3，树冠半径 = 冠宽 / 2；为 0 时按生长率画。DBH 仍然用族公式：它是 i-Tree 的输入，而且用了 i-Tree 的树干速率后，每年都等于 i-Tree 的 DBH。工具改动了一棵树的年龄（Growth Projections、自动年龄更新）时，会清掉它的 i-Tree 尺寸，直到重新计算，所以副本不会沿用原始树的尺寸。Import Shared Parameter 会把它们加到项目里，用于明细表。
- [x] **族的几何（2026-10-22，已载入沙盒；族文件本身没有保存）：**
  - `TreeOverall_Height = TreeFoliage_Height + TreeTrunk_Height`（原来加的是树干直径）。
  - `TreeTrunk_Diameter = TreeTrunk_DBH_Diameter`：画出来的树干跟着 DBH 变（原来每棵树都是 7.5 cm）。
  - `TreeRoot_Diameter` 至少是树干直径的 4 倍，这样很小的树的树根轮廓也不会反转。
  - `Mature Age` 和树高、冠宽的上限，把小于 1 m 的最大尺寸当作没填。
  - 载入前测试：每个类型 × 8 个年龄（0 到 40 年，有和没有 i-Tree 尺寸），没有失败。沙盒里 1,010 棵树全部保留，每棵都有树干、树冠和树根。
- [x] **最大尺寸被存成 mm（2026-10-22 修复）：** 沙盒里有 16 个类型的最大树高和冠宽是 mm（Prunus_Avium 20 mm × 12 mm），所以 `Mature Age` 为 1，这些类型的 75 棵树卡在第 1 年，只有 20 mm 高。已按 Airtable 表格（单位是米）改正（Prunus_Avium 20 m × 12 m，Mature Age 34）。Health Check 现在会提示最大尺寸小于 1 m 的树类型。
- [ ] **其他种植族：** 给它们加上同样的参数和公式。

## Phase 2：Design option 方案

Dashboard 上的每一个数字都取决于选对方案，所以这一步排在界面重新设计之前。在分支 `roadmap-phases-2-5` 上完成，尚未发布。

- [x] **方案选择器：** 每个 design option set 选一个 option，默认为各 set 的主选（primary）。一个方案 = 主模型 + 每个 set 中选中的 option；没被选中的 option 里的元素不计入。Revit 端插件会列出每个 set 和 option，包括空的（`DesignOptionSetItem`）。
- [x] **"Use current view"：** 根据当前视图里可见的 option 推算方案（每个 option 用一个视图收集器；某个 set 在视图里什么都看不到时，退回它的主选）。Edit… 里也有，名为 "Fill from Revit's active view"。
- [x] **各方案的树冠覆盖率：** 树冠投影的并集，重叠只算一次（精确计算，`CanopyUnion`），÷ 所选场地面积。树现在带有平面位置；没有位置信息的缓存快照退回按面积相加。
- [x] **绿地比例：** 透水 ÷（透水 + 不透水）floor 面积，显示在 Site & Biodiversity 和方案总览中。未分类的 floor 和建筑楼板（结构楼板、没有 LDS 类型的 "slab"/"suspended" 等类型，或标为 `Building` 的）单独列出，不计入。
- [x] **"Design option" 指的是方案：** 保存的方案有名字，存在项目里（`ProjectSettingsSnapshot.DesignScenarios`），显示为例如 "Scheme A = Variante 1: Baseline + Test: Option 1"。Dashboard、BNG 页（作为筛选）和 JSON 导出（schema v5，`designOption` + `choices`）都按方案工作。
- [x] **方案总览：** 一张表并排列出所有保存的方案，以及与所选参照方案的差值（绝对值和百分比；百分比类指标用百分点）。参照方案保存在项目里。
- [x] **性能对比：** 每个方案的树、树种、树冠覆盖率、绿地比例、CO₂e、避免的径流、去除的污染物、氧气、BNG 单位/净增益、隐含碳、生态系统价值、维护成本和净值，并排显示并配柱状图（隐含碳和维护成本越低越好）。

## Phase 3：Dashboard 重新设计

按标注过的截图实施。在分支 `roadmap-phases-2-5` 上完成，尚未发布。

- [x] **生态系统价值横幅：** 拆分为 CO₂ 吸收、雨水径流和污染物三项价值（树，来自 i-Tree），加上种植区（WWP 表格只给一个合并的节省值），然后是维护成本和净值。
- [x] **累计量：** 每张效益卡片都显示展望期内的 kg 和 m³，不只是金额；碳卡片还显示释放的氧气。
- [x] **展望期：** CO₂e、径流和污染物的展望期并排显示。
- [x] **项目概况：** 树冠覆盖率（重叠只算一次）、水（避免的径流和截留的降雨）、树种数和生境类型数（有 BNG 生境时用 BNG，否则用种植类型）、种植面积和隐含碳。去掉了 "Pollution removed" 和 "Inventory"。
- [x] **就绪状态移到 Data Explorer**，放在状态卡片旁边。Dashboard 只保留结果。
- [x] **Data Explorer 布局：**
  - 单位放在列标题里，单元格是右对齐的纯数字（1,234 · 56.7 · 0.42），列宽足够不会截断。种植区的数值也跟随公制/英制切换。
  - 种植区表格放在树表格**下方**，两者都占满宽度，带有氧气、隐含碳和维护列。
- [x] **按年计的单位：** 碳、水和空气质量卡片在展望期总量旁边显示当前的年值（kg/年、m³/年）。
- [x] **维护成本：**
  - 保留导入费率原来的名字 `!_S_PLT_LDS_MaintenanceCostAnnual_Currency`（2026-10-21 决定：不改名）。
  - 按每棵树、以及种植区按每 m² × floor 面积计算，写入 `!_S_PLT_Result_MaintenanceCostAnnual_Currency`。Tree Calculator（任何实例写入）和 Floor Calculator 会更新它；Dashboard 根据费率实时计算。
  - Dashboard：每年的维护成本和展望期内的维护成本，以及**净值** = 生态系统价值 − 维护成本，显示在横幅和方案对比中。
- [x] **种植区构成：** 按面积列出种植类型，以及按面积列出 BNG 生境类型。
- [x] **树种排行：** 改为展望期内的逐树种表格——树的数量、节省的金额、CO₂e、径流、污染物和 O₂——可按任意列排序，默认按节省金额。
- [x] **树的氧气：** Tree Calculator 写入 `!_S_PLT_Result_OxygenProducedAnnual_Mass`（与种植区共用）和新的 `!_S_PLT_Result_OxygenProducedLifetimeTotal_Mass` = CO₂e × 32/44。按命名规则用 `Result_` 而不是 `iTreeResult_`。之前计算过的树在 Dashboard 上由它们的 CO₂e 推算。
- [x] **连通性：** 在 Executive overview 和 Site & Biodiversity 上显示各档的数量，并在 KPI Schedules 中加入档位/等级/分数（树和种植区）。
- [x] **说明文字：** 每张卡片说明它涵盖的范围——数量是树 + 种植区，金额和截留降雨只有树。
- [x] **所有工具统一的表格样式：** 泳道式的列分组，泳道之间留少许空白，两行表头（名称在上、单位在下），右对齐的纯数字——Health Check、Connectivity、Parameters、Tree Searcher、Growth Projections、Importer、Tree Calculator、iTree Downloader、Sync Audit、Floor Calculator（系数和 BNG）、Dashboard 的树种排行、方案总览和 Original data。共用样式在 `SharedUI/Styles.xaml`；`TableLanes.FollowRows` 让泳道在最后一行结束。BNG 页多余的列滚动条已去掉。
- [x] **KPI Schedules 是最后一个按钮**（Reporting 面板，在 Dashboard 之后），并涵盖最新的参数：CO₂ 当量、致敏性、连通性 dPC，以及种植区的 CO₂e 和 BNG（角色、基线生境/状况/单位、拟建生境、独特性、状况、战略重要性、生境单位、状态）。再次运行会**更新**之前生成的明细表——缺少的列插入到对应位置，保留筛选、分组、格式和图纸上的位置——而不是原样保留。
- [x] **Tree Searcher 记住手动匹配：** 已经有树种的类型显示为 "Saved match"，不再根据名字重新猜测；手动选择并指定的树种按族 : 类型记住（每个用户各自保存，`species-matches.db` 与其他本地 LIM 数据放在一起），下次在任何项目里都会作为 "Remembered match" 推荐。顺序：类型上保存的 → 记住的 → 按名字猜测。
- [x] **Importer 自动映射所有能映射的表格列：** 在 `_Rate_Number` 改名之前保存的映射会跟随改名（CO2 sequestration、runoff、oxygen、product 和 transport GWP），保留启用状态和换算方式；其他保存的映射如果参数已不存在，会退回默认映射，而不是留空。Sync Audit 以同样方式读取保存的映射。新参数 `!_S_PLT_LDS_PollutantsRemovedRate_Number` 让 `WWP_Pollutants_Removed` 像其他费率一样有了对应参数（需要装下一版插件，然后运行 Import Shared Parameter）；`WWP_Total_GWP` 会说明它是公式列，而不是显示 "Needs mapping"；参数还没绑定的列会提示运行 Import Shared Parameter。
- [x] **WWP 表格补上 i-Tree 缺的数据**（Tree Calculator 在 i-Tree 计算之后，或 Floor Calculator 计算 floor 时）：没有 i-Tree 结果的树会匹配到它在表格里的那一行（先按树种代码，再按类型或族名，最后用表格的 Generic 行），得到每棵树的 CO2e、碳、氧气、径流、污染物和节省金额，状态为 `Estimated`，不带输入签名，所以树一旦能计算就会被 i-Tree 结果替换；有 i-Tree 结果的树永远不会被覆盖。Dashboard 会计入估算的树（并说明数量），有一张 Estimated 状态卡片。每棵匹配上的树和每个种植区还会得到花粉量（新参数 `!_S_PLT_Result_PollenAnnual_Mass`），Site & Biodiversity 显示花粉负荷、致敏树和灌溉需求。顺带修复：表格里的金额是英镑文本（"£15.80"），Floor Calculator 从来没解析过，所以每个种植区的节省费用都是 0（现在会把英镑换算成项目货币，并在打开时重新同步旧缓存）；i-Tree 结果也会把污染物合计写入 `!_S_PLT_Result_PollutantsRemovedAnnual_Mass`。
- [x] **也从 WWP 表格匹配。** Tree Searcher：在已保存或已记住的匹配之后，表格按名字列出的类型（Types 列，例如 Acer_Campestre）会采用表格里的树种代码；那一行没有代码时，采用学名相同的 i-Tree 树种；最后才按名字在 i-Tree 目录里猜测（它先给族名打分，所以通用族名可能匹配错树种）。表格 45 行树里有 21 行带代码，其余按学名找到。Floor Calculator：表格中与 Revit floor 类型同名的行（"Planting type" 或 "Types"）优先；其次是去掉 WWP/Subs/Surface(s)/Vegetation 前缀后同名、且只有一行符合的（WWP_Subs_Raingarden、WWP_Surfaces_Asphalt）；最后才是模糊匹配。
- [x] **Dashboard 标出过期的树。** 以前它读的是保存的状态，所以 i-Tree 计算之后输入又变了的树仍然显示 Calculated（沙盒里有 520 棵）。现在报告会带上每棵树的 i-Tree 输入，Dashboard 像 Tree Calculator 一样推算状态：过期的树仍以旧的 i-Tree 结果计入合计，并显示红色提示条，说明数量和重算方法（Tree Calculator > Validate > Calculate valid），还有一个按钮可以在 Data explorer 里列出它们。完全没有结果的树显示琥珀色提示条。（Revit 端插件有改动。）
- [x] **维护成本卡片：** 在碳、雨水和空气污染旁边加了第四张分类卡片：展望期内的维护成本、树和种植区各自的维护成本、净值，以及当前的年费率。
- [x] **JSON 导出与网页版仪表板（2026-10-21）：** 导出原来按每个生长年份的副本求和，在树干生长率为 0 时，网页版每个年份显示的效益都一样。现在和 Dashboard 一样，用每棵种下的树在该年份的 i-Tree 预测（缓存的沙盒快照中，0 到 25 年为 9,323 到 34,524 kg CO2e）。Schema 6 新增过期和表格估算的树数量、花粉、致敏和灌溉数据，以及本地种比例的分母；之前的字段都没变。网页版仪表板（LandscapeCoBenefitsDashboard，已在本地提交，未推送）顶部是维护成本和净值面板，会提示过期、缺结果和估算的树，并新增花粉、致敏树和低需水树的卡片。已在无头 Edge 中检查：schema 6 的导出和自带的 v3 示例都能加载，任何生长年份和期间都没有脚本错误、NaN 或 undefined。

## Phase 4：用 i-Tree 逐年预测代替生长副本

为了出图，每个生长年份仍然以副本的形式画在各自的 `LIM_Growth_<N>yr` workset 上，但数字现在来自 i-Tree 自己的预测。在分支 `roadmap-phases-2-5` 上完成，尚未发布。

- [x] **原因：** 展望期是累计值（第 1 年 + 第 2 年 + … + 第 N 年）。"年值 × 年数"会少算正在生长的树，而第 N 年副本的"年值 × N"又会多算。
- [x] **i-Tree 逐年预测（只运行一次，不需要副本）：**
  - Tree Calculator 和 Growth Projections 的 "i-Tree by growth year" 都会在常规计算之后运行它（`ITreeTimelineCalculator`，通过 `ITreeBatchCalculator` 共用）。
  - 每个**唯一的种植输入**只请求一次：树种、种植时的 DBH、树况、冠层光照暴露度、位置和预测年限（`TreeTimelines.Key`）。输入相同的树共用一次结果。2026-10-21 实测：Acer campestre，25 年，2.8 秒。
  - **预测年限：族的 `Mature Age` − `AgeAtPlanting`，最多 35 年**；族没有成熟年龄时为 25 年（族自身的上限）。超过年限后效益保持在成熟时的值。
  - 每个输入在项目里只存一份（一个 DataStorage 元素上的 extensible storage，vendor EGIS：`TreeTimelineStore`）。每棵种下的树带有 `!_S_PLT_Result_iTreeTimelineKey_Text` 以及它 5/10/20/25 年的合计：`Result_CO2Equivalent{N}yr_Mass`、`Result_RunoffAvoided{N}yr_Volume`、`Result_PollutantsRemoved{N}yr_Mass`、`Result_CostSaved{N}yr_Currency`（KPI Schedules 里也有）。
  - Dashboard 和方案对比从预测中读取任意展望年份（`TreeOutlook`），数字里不计入生长副本；生长年份 workset 和"年值 × 年数"作为兜底，展望期说明会写明用的是哪一种。
  - 只在输入变化时重新运行：已保存的 key 不会再次请求。
  - 数据来源：**效益以 i-Tree 预测为准，几何以族为准**（推荐的做法）。
  - floor 不需要这个：它们的效益是 Airtable 费率 × 面积，每年都一样。
- [x] **全寿命参数：** 对种下的树，`…LifetimeTotal` 现在是整个预测期（从种植到成熟），每棵树的时间跨度相同。生长年份副本保留各自按年份的计算。
- [ ] **检查族的数据：** 两个 Prunus avium 类型的 `Mature Age` = 1，小于它们的种植年龄 5，所以预测只有一年。可能是最大高度的单位写错了。表格是对的（2026-10-21 检查：Prunus_Avium 和 Prunus_Avium_Plena，最大高度 20 m，高度年生长 0.6 m，所以 `Mature Age` 应为 34）；还要检查：Revit 里这两个类型的最大高度和高度比例。

## Phase 5：在 Revit 中实测

模型："LAN_LIM Arboris_EWR Sandbox"（协同工作，ACC）。测试时可以写入，但**绝不 Sync with Central**。

2026-10-21 用脚本检查，当时 Revit 里运行的还是 v1.4 的插件：

- [x] 从 Revit 读取 design option set，包括还没有内容的 option（Variante 1: Baseline / Option 1 / Option 2；Test: Option 1 / Option 2）。
- [x] 带 `ElementDesignOptionFilter` 的视图收集器只返回当前视图显示的 option，"Use current view" 就是这样推算方案的。
- [x] 新版 Dashboard 通过旧插件连接实时模型也能运行（旧的报告仍能加载）：方案选择器、Edit… 对话框、方案总览、重新设计的总览和 Data explorer。
- [x] Revit 能解析更新后的 `Shared_Parameters_WWP.txt`（160 个定义；新参数按预期是 Mass/Volume/Currency/Text）。
- [x] `HOST_AREA_COMPUTED` 与 floor 的 Area 一致，所以每 m² 维护费 × 面积用的是正确的面积。
- [x] 一个种植输入的 i-Tree 实时预测：25 年用时 2.8 秒，逐年解析。

2026-10-21 安装新版本后实测（通过插件的管道驱动，以及 Dashboard 和 Floor Calculator 的界面）。在沙盒里加了测试数据，**未保存也未同步**：Variante 1 Option 1 = Baseline 的 541 棵树，每隔一棵换成 Quercus robur；Variante 1 Option 2 = Baseline 每三棵取一棵（181 棵）；Test Option 2 = Test Option 1 的 541 棵树，三分之一是 Betula，没有种植区；四个保存的方案（Scheme A–D）；一个 "LIM test - scenario view" 三维视图。

- [x] **Import Shared Parameter：** 19 个新参数绑定成功（"Created: 19"），没有冲突。
- [x] **Tree Calculator：** 2,949 棵树有 13 个唯一的 i-Tree 输入；预测覆盖 10 个唯一的种植输入（预测年限 1–35 年），写入每棵种下的树；生长副本没有 key。第二次运行什么都没请求（"already stored"）。数值与单独运行的预测一致（Acer campestre 10 年 44.7 kg CO₂e，25 年 165.8 kg；全寿命 = 25 年的预测）。
- [x] **维护成本：** 树保存 £20/年；floor 保存费率 × 面积（例如湿地 £8 × 20,406 m²），与 Dashboard 的实时合计一致（£3.64M/年；沙盒的 floor 有整个场地那么大）。通过实例写入器测试；Floor Calculator 自己运行时写法相同。
- [x] **Dashboard：** 列出每个 option，包括空的；在显示 Option 1 的视图里，"Use current view" 得到 Variante 1 = Option 1；四个保存的方案以 Scheme A 为参照进行对比（表格和图表）；展望期说明显示 "i-Tree's year-by-year forecast"；JSON 导出为 schema 5，4 个方案 × 4 个生长年份，带 `designOption` 和 `choices`。
- [x] **BNG 页：** 方案筛选列出保存的方案；Scheme A +13.2%，Scheme D −24.4%，与 Dashboard 一致。
- [x] **KPI Schedules：** 重建了 9 个（包括新的 option），带连通性、氧气、维护和预测列。
- [x] **Connectivity：** 各档数量显示在 Dashboard 上（312 个关键、537 个强、453 个中等、321 个弱、43 个未评估）。
- [x] **Growth Projections：** 对未激活的 design option，运行会把原始树移到 Baseline workset，并跳过副本，提示需要激活该 option；激活后，在该 option 里的正确 workset 上生成了 543 个副本（ProjectedYears 10 → Years 15）；再次运行更新了这 543 个，没有新建。
- 2026-10-22 修复：Dashboard、Floor Calculator 和 i-Tree Calculator 每次关闭都会崩溃（窗口已经关掉后才取消主题订阅）；现在订阅一直保留到窗口关闭。
- 测试中修复的问题：Floor Calculator 启动时崩溃（增量构建产生了不完整的 `.pri`——干净重建后解决；现在启动崩溃会记录到 `%TEMP%`）；"Raingarden" 和湿地类型现在算作透水；`WWP_Surfaces_Building` 类型的 floor（匹配到 Asphalt）作为建筑不计入；KPI 明细表和 Growth Projections 的标签不再重复两次 "primary"（Revit 端插件有改动：需要装下一版，然后重建 KPI 明细表）。
- [ ] **Excel Importer** 没有运行（维护费率已经在类型上了）。
- [ ] 用真实的种植族核对成熟尺寸的计算（见 Phase 4 中 Prunus avium `Mature Age` = 1 的说明）。

## 后续事项

- [ ] **Growth Projections — "移除不再列出的年份"：** 删除这些年份的副本，然后删除空的 workset。
- [ ] **Growth Projections — 放错 option 的副本：** 副本和原树不在同一个 design option 时，在正确的 option 激活后，删除并在正确的 option 里重新生成。
- [ ] **Refresh and Audit：** 应用新的表格费率之后，自动重算受影响的 floor，让 floor 的合计永远不过期。
- [ ] **Importer：** 参数改名时，可以选择把数值复制到新参数，而不是重新计算。目前的做法：重新计算。

## WWP 景观数据表格（Airtable）里的数据问题

- [ ] **`WWP_Vegetation_Shrub` 的避免径流：** 0.83 看起来是每株灌木的值，而不是每 m²（其他植被在 0.04–0.49 之间）。已于 2026-10-16 在 Airtable 中修改，待确认。
- [ ] **`f_int and Crg…` 径流系数：** Meadow 原来是 `7%`，应为 `0.07`。已于 2026-10-16 在 Airtable 中修改，待确认。
- [ ] **金额列：** `COST SAVED per year …` 里是范围（`£20-60`），无法作为数字导入。LIM 现在能读表格里的金额（"£15.80"），对缓存的表格取范围的中间值，但按效益分项的金额加起来仍然不等于 `COST_SAVED`。
- [ ] **`WWP_Total_GWP`：** 在 "Imported table" 里是普通数字列（2026-10-21 通过 Airtable schema 检查），不是公式，而且 Product/Transport GWP 是空的。它的值等于隐含碳 − 每年的 CO₂ 固定量：Asphalt 18 − 0 = 18，Acer campestre 0 − 28 = −28。LIM 只使用正的（隐含碳）部分。

## 修改规则

### 参数名

- **计算值：** 以 `!_S_PLT_Result_…` 开头。为兼容现有项目保留的例外：`!_S_PLT_iTreeResult_*`、`!_S_PLT_BNGResult_*` 和 `!_S_PLT_ConnectivityResult_*`。
- **表格参考费率：** 按每棵树或每 m²，放在类型上，命名为 `…Rate_Number`，不用 `_Mass`/`_Volume`。
- **表格里的公式列：** 不设参数，由 LIM 计算。

### 参数改名或改类型

- 给它一个**新的 GUID**，把旧名字加到 `SharedParameterSetupService` 的 `legacyAliases` 里，并从 `Shared_Parameters_WWP.txt` 中删除旧的那一行。
- **绝不**把一个 GUID 用在新名字上，也绝不给已有的名字换新 GUID。项目按 GUID 识别参数，两种做法都会破坏项目。
- 如果名字改回了以前用过的某个名字，沿用它最初发布时的 GUID。
- 安全网：`ResultParameterReusePolicy` 允许 Import Shared Parameter 保留一个名字和类型都匹配、但 GUID 不同的现有 BNG/Connectivity *结果*参数。它是为用旧目录绑定的项目准备的，不能代替上面的规则。

### 共享参数文件

- `Shared_Parameters_WWP.txt` 是带 BOM 的 UTF-16 LE 编码，换行为 CRLF。只能用脚本编辑，不要在编辑器里手动改。
- 它在构建时被嵌入 Revit 端插件，所以要在运行 `Package-Addin.ps1` 之前更新它。
