using ClosedXML.Excel;

const string sharedGroupName = "PLANTING - iTree";
const string projectInformationCategory = "OST_ProjectInformation";
const string plantingCategory = "OST_Planting";

if (args.Length < 2)
{
    throw new ArgumentException(
        "Usage: ITreeParameterImportWorkbookGenerator <shared-parameter-file> <output-workbook>");
}

var sharedParameterPath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
if (!File.Exists(sharedParameterPath))
{
    throw new FileNotFoundException("Shared parameter file was not found.", sharedParameterPath);
}

Directory.CreateDirectory(
    Path.GetDirectoryName(outputPath)
    ?? throw new InvalidOperationException("The output workbook directory is invalid."));

var parameters = ReadSharedParameters(sharedParameterPath, sharedGroupName)
    .Select(CreateBinding)
    .OrderBy(binding => BindingOrder(binding.Scope))
    .ThenBy(binding => binding.RevitGroup, StringComparer.Ordinal)
    .ThenBy(binding => binding.Name, StringComparer.Ordinal)
    .ToList();

if (parameters.Count != 39)
{
    throw new InvalidOperationException(
        $"Expected 39 shared parameters in '{sharedGroupName}', but found {parameters.Count}.");
}

WriteWorkbook(outputPath, sharedParameterPath, parameters);
VerifyWorkbook(outputPath, parameters);

Console.WriteLine($"Workbook: {outputPath}");
Console.WriteLine($"Shared parameters: {sharedParameterPath}");
Console.WriteLine($"Binding rows: {parameters.Count}");
Console.WriteLine(
    $"Project Information: {parameters.Count(item => item.CategoryApi == projectInformationCategory)}; " +
    $"Planting Type: {parameters.Count(item => item.CategoryApi == plantingCategory && item.Scope == "Type")}; " +
    $"Planting Instance: {parameters.Count(item => item.CategoryApi == plantingCategory && item.Scope == "Instance")}");

return;

static IReadOnlyList<SharedParameter> ReadSharedParameters(string path, string groupName)
{
    var lines = File.ReadAllLines(path);
    var groupIds = lines
        .Where(line => line.StartsWith("GROUP\t", StringComparison.Ordinal))
        .Select(line => line.Split('\t'))
        .Where(columns => columns.Length >= 3 &&
                          string.Equals(columns[2], groupName, StringComparison.OrdinalIgnoreCase))
        .Select(columns => columns[1])
        .ToHashSet(StringComparer.Ordinal);
    if (groupIds.Count != 1)
    {
        throw new InvalidOperationException(
            $"Expected one shared-parameter group named '{groupName}', but found {groupIds.Count}.");
    }

    return lines
        .Where(line => line.StartsWith("PARAM\t", StringComparison.Ordinal))
        .Select(line => line.Split('\t'))
        .Where(columns => columns.Length >= 8 && groupIds.Contains(columns[5]))
        .Select(columns => new SharedParameter(
            columns[1],
            columns[2],
            columns[3],
            columns[7]))
        .ToList();
}

static ParameterBinding CreateBinding(SharedParameter parameter)
{
    var scope = ParameterSchema.TypeParameters.Contains(parameter.Name)
        ? "Type"
        : "Instance";
    var category = ParameterSchema.ProjectInformationParameters.Contains(parameter.Name)
        ? projectInformationCategory
        : plantingCategory;
    var group = GetRevitGroup(parameter.Name);

    return new ParameterBinding(
        parameter.Guid,
        parameter.Name,
        sharedGroupName,
        parameter.Description,
        ToRevitTypeLabel(parameter.DataType),
        parameter.DataType,
        group,
        scope,
        category,
        ParameterSchema.SuggestedSourceColumns.TryGetValue(parameter.Name, out var source)
            ? source
            : string.Empty);
}

static string GetRevitGroup(string name)
{
    if (ParameterSchema.ProjectInformationParameters.Contains(name) ||
        name.Contains("_iTreeSpecies_", StringComparison.Ordinal) ||
        name.EndsWith("_DataSync_SourceName_Text", StringComparison.Ordinal) ||
        name.EndsWith("_DataSync_SourceRecordId_Text", StringComparison.Ordinal))
    {
        return "PG_IDENTITY_DATA";
    }

    if (name.Contains("_GrowthRatio_", StringComparison.Ordinal) ||
        name.EndsWith("_TreeGrowth_Years_Number", StringComparison.Ordinal))
    {
        return "PG_CONSTRAINTS";
    }

    if (name.Contains("_TreeFoliage_", StringComparison.Ordinal) ||
        name.Contains("_TreeTrunk_", StringComparison.Ordinal) ||
        name.Contains("_TreeOverall_", StringComparison.Ordinal))
    {
        return "PG_GEOMETRY";
    }

    if (name.Contains("_iTreeCarbon_", StringComparison.Ordinal) ||
        name.Contains("_iTreeAir_", StringComparison.Ordinal) ||
        name.Contains("_iTreeWater_", StringComparison.Ordinal))
    {
        return "PG_GREEN_BUILDING";
    }

    return "PG_DATA";
}

static string ToRevitTypeLabel(string sharedType) => sharedType switch
{
    "TEXT" => "Text",
    "NUMBER" => "Number",
    "INTEGER" => "Integer",
    "LENGTH" => "Length",
    "VOLUME" => "Volume",
    _ => sharedType
};

static int BindingOrder(string scope) => scope == "Type" ? 1 : 2;

static void WriteWorkbook(
    string path,
    string sharedParameterPath,
    IReadOnlyList<ParameterBinding> parameters)
{
    using var workbook = new XLWorkbook();
    var sheet = workbook.AddWorksheet("Project Parameters");
    var headers = new[]
    {
        "Parameter Name",
        "Group Name",
        "Description",
        "ParaTypeRevit",
        "Data Type",
        "Parameter Group",
        "Instance / Type",
        "Category",
        "Source Column",
        "Category API"
    };
    sheet.Cell(1, 1).InsertData(new[] { headers });

    var rows = parameters.Select(item => new object?[]
    {
        item.Name,
        item.SharedGroup,
        item.Description,
        item.RevitTypeLabel,
        item.SharedDataType,
        item.RevitGroup,
        item.Scope,
        item.CategoryApi,
        item.SourceColumn,
        item.CategoryApi
    });
    sheet.Cell(2, 1).InsertData(rows);

    var lastRow = parameters.Count + 1;
    var header = sheet.Range(1, 1, 1, headers.Length);
    header.Style.Font.Bold = true;
    header.Style.Font.FontColor = XLColor.White;
    header.Style.Fill.BackgroundColor = XLColor.FromHtml("#004B6B");
    header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    header.Style.Alignment.WrapText = true;
    header.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
    header.Style.Border.BottomBorderColor = XLColor.FromHtml("#79B7CB");
    sheet.Row(1).Height = 30;

    var data = sheet.Range(2, 1, lastRow, headers.Length);
    data.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    data.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
    data.Style.Border.BottomBorderColor = XLColor.FromHtml("#D9E2E6");
    sheet.Range(2, 3, lastRow, 3).Style.Alignment.WrapText = true;
    sheet.Range(2, 9, lastRow, 9).Style.Alignment.WrapText = true;

    sheet.SheetView.FreezeRows(1);
    sheet.SheetView.FreezeColumns(2);
    sheet.ShowGridLines = false;
    var table = sheet.Range(1, 1, lastRow, headers.Length)
        .CreateTable("PlantingiTreeProjectParameters");
    table.Theme = XLTableTheme.TableStyleMedium2;

    sheet.Column(1).Width = 62;
    sheet.Column(2).Width = 22;
    sheet.Column(3).Width = 68;
    sheet.Column(4).Width = 16;
    sheet.Column(5).Width = 14;
    sheet.Column(6).Width = 24;
    sheet.Column(7).Width = 16;
    sheet.Column(8).Width = 27;
    sheet.Column(9).Width = 30;
    sheet.Column(10).Width = 27;
    sheet.Rows(2, lastRow).AdjustToContents(18, 54);
    sheet.AutoFilter.Clear();
    table.ShowAutoFilter = true;

    var instructions = workbook.AddWorksheet("Instructions");
    instructions.ShowGridLines = false;
    instructions.Cell("A1").Value = "WWP Planting / i-Tree Project Parameter Import";
    instructions.Cell("A1").Style.Font.Bold = true;
    instructions.Cell("A1").Style.Font.FontSize = 18;
    instructions.Cell("A1").Style.Font.FontColor = XLColor.White;
    instructions.Cell("A1").Style.Fill.BackgroundColor = XLColor.FromHtml("#004B6B");
    instructions.Range("A1:F1").Merge();
    instructions.Row(1).Height = 32;

    var guidance = new object?[][]
    {
        ["Purpose", "Creates the required Project Information, Planting Type, and Planting Instance bindings for the i-Tree workflow."],
        ["Shared parameter file", Path.GetFileName(sharedParameterPath)],
        ["Shared parameter group", sharedGroupName],
        ["Required Revit tool", "WWPTools > Setup > Add Project Parameter > Bulk Import Shared Parameters"],
        ["Step 1", "Select the shared parameter TXT file shown above."],
        ["Step 2", "Choose Import parameters from Excel and select this workbook."],
        ["Step 3", "Confirm all imported rows have no review warning, then run Import parameters."],
        ["Category notation", "BuiltInCategory API names are intentionally used so the mapping does not depend on the Revit display language."],
        ["Project Information", "Latitude, longitude, and preferred unit system are Instance bindings to OST_ProjectInformation."],
        ["Planting Type", "Species identity, growth ratios, and external source/audit identity are Type bindings to OST_Planting."],
        ["Planting Instance", "Years, condition, live geometry, i-Tree results, and calculation status are Instance bindings to OST_Planting."],
        ["Parameter groups", "Identity Data = identity/location; Constraints = growth inputs; Geometry = live dimensions; Green Building = environmental results; Data = status and synchronization."],
        ["Important", "If a parameter already exists with the same name but another GUID, the importer will report it instead of replacing that definition."]
    };
    instructions.Cell("A3").InsertData(guidance);
    instructions.Range("A3:A15").Style.Font.Bold = true;
    instructions.Range("A3:A15").Style.Fill.BackgroundColor = XLColor.FromHtml("#E6F2F7");
    instructions.Range("A3:B15").Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    instructions.Range("A3:B15").Style.Alignment.WrapText = true;
    instructions.Range("A3:B15").Style.Border.BottomBorder = XLBorderStyleValues.Hair;
    instructions.Range("A3:B15").Style.Border.BottomBorderColor = XLColor.FromHtml("#D9E2E6");
    instructions.Column(1).Width = 28;
    instructions.Column(2).Width = 105;
    instructions.Rows(3, 15).AdjustToContents(22, 64);
    instructions.SheetView.FreezeRows(1);

    workbook.SaveAs(path);
}

static void VerifyWorkbook(string path, IReadOnlyList<ParameterBinding> expected)
{
    using var workbook = new XLWorkbook(path);
    var sheet = workbook.Worksheet("Project Parameters");
    var lastRow = sheet.LastRowUsed()?.RowNumber()
                  ?? throw new InvalidOperationException("Generated workbook is empty.");
    if (lastRow - 1 != expected.Count)
    {
        throw new InvalidOperationException(
            $"Generated workbook contains {lastRow - 1} binding rows; expected {expected.Count}.");
    }

    var headers = sheet.Row(1).Cells(1, 10)
        .Select(cell => cell.GetString())
        .ToList();
    string[] requiredHeaders =
    [
        "Parameter Name",
        "Group Name",
        "Parameter Group",
        "Instance / Type",
        "Category"
    ];
    var missingHeaders = requiredHeaders
        .Where(header => !headers.Contains(header, StringComparer.Ordinal))
        .ToList();
    if (missingHeaders.Count > 0)
    {
        throw new InvalidOperationException(
            $"Generated workbook is missing headers: {string.Join(", ", missingHeaders)}.");
    }

    var rows = sheet.Rows(2, lastRow)
        .Select(row => new
        {
            Name = row.Cell(1).GetString(),
            SharedGroup = row.Cell(2).GetString(),
            RevitGroup = row.Cell(6).GetString(),
            Scope = row.Cell(7).GetString(),
            Category = row.Cell(8).GetString()
        })
        .ToList();
    if (rows.Any(row =>
            string.IsNullOrWhiteSpace(row.Name) ||
            row.SharedGroup != sharedGroupName ||
            row.Scope is not ("Type" or "Instance") ||
            string.IsNullOrWhiteSpace(row.RevitGroup) ||
            row.Category is not (plantingCategory or projectInformationCategory)))
    {
        throw new InvalidOperationException(
            "Generated workbook contains an incomplete or unsupported binding row.");
    }

    var duplicate = rows
        .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault(group => group.Count() > 1);
    if (duplicate is not null)
    {
        throw new InvalidOperationException(
            $"Generated workbook contains duplicate binding rows for '{duplicate.Key}'.");
    }
}

internal static class ParameterSchema
{
    public static readonly HashSet<string> ProjectInformationParameters =
    [
        "!_S_PLANTING_iTreeLocation_Latitude_Number",
        "!_S_PLANTING_iTreeLocation_Longitude_Number",
        "!_S_PLANTING_iTreeUnits_PreferredSystem_Text"
    ];

    public static readonly HashSet<string> TypeParameters =
    [
        "!_S_PLANTING_iTreeSpecies_Code_Text",
        "!_S_PLANTING_iTreeSpecies_ScientificName_Text",
        "!_S_PLANTING_iTreeSpecies_CommonName_Text",
        "!_S_PLANTING_iTreeSpecies_Type_Text",
        "!_S_PLANTING_iTreeSpecies_ReplaceBy_Text",
        "!_S_PLANTING_GrowthRatio_HeightbyYear_Number",
        "!_S_PLANTING_GrowthRatio_WidthbyYear_Number",
        "!_S_PLANTING_GrowthRatio_TrunkDiameterbyYear_Number",
        "!_S_PLANTING_DataSync_SourceName_Text",
        "!_S_PLANTING_DataSync_SourceRecordId_Text",
        "!_S_PLANTING_DataSync_Status_Text",
        "!_S_PLANTING_DataSync_LastUpdated_Text",
        "!_S_PLANTING_DataSync_InputSignature_Text"
    ];

    public static readonly IReadOnlyDictionary<string, string> SuggestedSourceColumns =
        new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["!_S_PLANTING_iTreeLocation_Latitude_Number"] = "Latitude",
        ["!_S_PLANTING_iTreeLocation_Longitude_Number"] = "Longitude",
        ["!_S_PLANTING_iTreeSpecies_Code_Text"] = "Species_Code",
        ["!_S_PLANTING_iTreeSpecies_ScientificName_Text"] = "Scientific_Name",
        ["!_S_PLANTING_iTreeSpecies_CommonName_Text"] = "Common_Name",
        ["!_S_PLANTING_iTreeSpecies_Type_Text"] = "Species_Type",
        ["!_S_PLANTING_iTreeSpecies_ReplaceBy_Text"] = "Replace_By",
        ["!_S_PLANTING_TreeGrowth_Years_Number"] = "Years",
        ["!_S_PLANTING_iTreeInput_Condition_Text"] = "Tree_Condition",
        ["!_S_PLANTING_iTreeInput_CrownExposure_Number"] = "Crown_Exposure",
        ["!_S_PLANTING_TreeFoliage_Width"] = "Crown_Width",
        ["!_S_PLANTING_TreeFoliage_Height"] = "Crown_Height",
        ["!_S_PLANTING_TreeTrunk_Height"] = "Trunk_Height",
        ["!_S_PLANTING_TreeTrunk_Diameter"] = "Trunk_Diameter",
        ["!_S_PLANTING_TreeTrunk_DBH_Diameter"] = "DBH",
        ["!_S_PLANTING_TreeOverall_Height"] = "Height",
        ["!_S_PLANTING_GrowthRatio_HeightbyYear_Number"] = "GrowthRatio_HeightbyYear",
        ["!_S_PLANTING_GrowthRatio_WidthbyYear_Number"] = "GrowthRatio_WidthbyYear",
        ["!_S_PLANTING_GrowthRatio_TrunkDiameterbyYear_Number"] = "GrowthRatio_TrunkDiameterbyYear",
        ["!_S_PLANTING_iTreeUnits_PreferredSystem_Text"] = "Unit System"
    };
}

internal sealed record SharedParameter(
    string Guid,
    string Name,
    string DataType,
    string Description);

internal sealed record ParameterBinding(
    string Guid,
    string Name,
    string SharedGroup,
    string Description,
    string RevitTypeLabel,
    string SharedDataType,
    string RevitGroup,
    string Scope,
    string CategoryApi,
    string SourceColumn);
