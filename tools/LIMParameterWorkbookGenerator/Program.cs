using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;

const string sharedGroup = "LIM Landscape Data";
var outputDirectory = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Deployment"));
var baseSharedParametersPath = args.Length > 1 && File.Exists(args[1])
    ? Path.GetFullPath(args[1])
    : null;
Directory.CreateDirectory(outputDirectory);

var workbookPath = Path.Combine(outputDirectory, "LIM Landscape Project Parameters.xlsx");
var sharedParametersPath = Path.Combine(outputDirectory, "LIM Landscape SharedParameters.txt");

IReadOnlyList<CategoryBinding> both =
[
    new("Planting", "OST_Planting"),
    new("Floors", "OST_Floors")
];

IReadOnlyList<CategoryBinding> plantingOnly =
[
    new("Planting", "OST_Planting")
];

var definitions = new[]
{
    new Definition("WWP_LDS_CalculationType", "TEXT", "Text", "Calculation mode used to distinguish trees from area-based landscape types.", "WWP_LDS_CalculationType", both),
    new Definition("WWP_LDS_Category", "TEXT", "Text", "Primary LIM landscape classification.", "WWP_LDS_Category", both),
    new Definition("WWP_LDS_SubCategory", "TEXT", "Text", "Secondary LIM landscape classification or species/type label.", "WWP_LDS_SubCategory", both),
    new Definition("WWP_Avoided_Water_Runoff", "NUMBER", "Number", "Annual avoided water runoff coefficient.", "Avoided runoff m3/yr (16/18 girth)", both),
    new Definition("WWP_Carbon_Dioxide_Sequestration", "NUMBER", "Number", "Annual carbon dioxide sequestration coefficient.", "Carbon dioxide sequestration kgCO2e/(m2)/yr (16/18 girth)", both),
    new Definition("WWP_Oxygen_Levels", "NUMBER", "Number", "Annual oxygen-production coefficient.", "Oxygen levels O2 kg/yr (16/18 girth)", both),
    new Definition("WWP_Pollutants_Removed", "NUMBER", "Number", "Annual pollutants-removed coefficient.", "WWP_Pollutants_Removed", both),
    new Definition("WWP_Maintenance_Cost", "CURRENCY", "Currency", "Annual landscape maintenance cost.", "Maintenance Costs", both),
    new Definition("WWP_Cost_Saved", "CURRENCY", "Currency", "Annual landscape cost savings.", "COST_SAVED", both),
    new Definition("WWP_Total_GWP", "NUMBER", "Number", "Total global-warming-potential value used by the LIM workflow.", "WWP_Total_GWP", both),
    new Definition("Maxi_Height", "LENGTH", "Length", "Maximum mature planting height.", "Max_Height", plantingOnly),
    new Definition("Maxi_Width", "LENGTH", "Length", "Maximum mature planting width.", "Max_Width", plantingOnly)
};

var sharedResult = WriteSharedParametersFile(sharedParametersPath, definitions, baseSharedParametersPath);
WriteWorkbook(workbookPath, sharedParametersPath, definitions);

using (var verification = new XLWorkbook(workbookPath))
{
    var sheet = verification.Worksheet("Project Parameters");
    var importedRows = sheet.LastRowUsed()?.RowNumber() - 1 ?? 0;
    if (importedRows != definitions.Sum(definition => definition.Categories.Count))
    {
        throw new InvalidOperationException("Generated workbook row count did not pass verification.");
    }
}

Console.WriteLine($"Workbook: {workbookPath}");
Console.WriteLine($"Shared parameters: {sharedParametersPath}");
Console.WriteLine($"Definitions: {definitions.Length}; binding rows: {definitions.Sum(definition => definition.Categories.Count)}");
Console.WriteLine($"Shared definitions reused: {sharedResult.Reused}; added: {sharedResult.Added}; base: {baseSharedParametersPath ?? "none"}");

return;

(int Reused, int Added) WriteSharedParametersFile(
    string path,
    IReadOnlyList<Definition> items,
    string? baseFilePath)
{
    var lines = baseFilePath is null
        ? new List<string>
        {
            "# This is a Revit shared parameter file.",
            "# Generated for the LIM Landscape Data workflow. Do not edit GUIDs after deployment.",
            "*META\tVERSION\tMINVERSION",
            "META\t2\t1",
            "*GROUP\tID\tNAME",
            "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE"
        }
        : File.ReadAllLines(baseFilePath, Encoding.UTF8).ToList();

    var groupRows = lines
        .Where(line => line.StartsWith("GROUP\t", StringComparison.Ordinal))
        .Select(line => line.Split('\t'))
        .Where(columns => columns.Length >= 3)
        .ToList();
    var existingGroup = groupRows.FirstOrDefault(columns =>
        string.Equals(columns[2], sharedGroup, StringComparison.OrdinalIgnoreCase));
    var groupId = existingGroup?[1];
    if (string.IsNullOrWhiteSpace(groupId))
    {
        var maximumGroupId = groupRows
            .Select(columns => int.TryParse(columns[1], out var id) ? id : 0)
            .DefaultIfEmpty()
            .Max();
        groupId = (maximumGroupId + 1).ToString();
        var parameterHeaderIndex = lines.FindIndex(line => line.StartsWith("*PARAM\t", StringComparison.Ordinal));
        if (parameterHeaderIndex < 0)
        {
            throw new InvalidOperationException("The base shared-parameter file has no *PARAM header.");
        }

        lines.Insert(parameterHeaderIndex, $"GROUP\t{groupId}\t{sharedGroup}");
    }

    var existingDefinitions = lines
        .Where(line => line.StartsWith("PARAM\t", StringComparison.Ordinal))
        .Select(line => line.Split('\t'))
        .Where(columns => columns.Length >= 4)
        .GroupBy(columns => columns[2], StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    var reused = 0;
    var added = 0;

    foreach (var item in items)
    {
        if (existingDefinitions.TryGetValue(item.Name, out var existing))
        {
            if (!string.Equals(existing[3], item.SharedDataType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Existing shared parameter '{item.Name}' uses data type '{existing[3]}', expected '{item.SharedDataType}'.");
            }

            reused++;
            continue;
        }

        lines.Add(string.Join('\t',
            "PARAM",
            CreateStableGuid(item.Name),
            item.Name,
            item.SharedDataType,
            string.Empty,
            groupId,
            "1",
            item.Description,
            "1",
            "0"));
        added++;
    }

    File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    return (reused, added);
}

void WriteWorkbook(string path, string sharedPath, IReadOnlyList<Definition> items)
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
        "Group",
        "Instance Parameter",
        "Category",
        "Source Column",
        "Category API"
    };

    for (var column = 0; column < headers.Length; column++)
    {
        sheet.Cell(1, column + 1).Value = headers[column];
    }

    var row = 2;
    foreach (var item in items)
    {
        foreach (var category in item.Categories)
        {
            sheet.Cell(row, 1).Value = item.Name;
            sheet.Cell(row, 2).Value = sharedGroup;
            sheet.Cell(row, 3).Value = item.Description;
            sheet.Cell(row, 4).Value = item.RevitTypeLabel;
            sheet.Cell(row, 5).Value = item.SharedDataType;
            sheet.Cell(row, 6).Value = "PG_IDENTITY_DATA";
            sheet.Cell(row, 7).Value = false;
            sheet.Cell(row, 8).Value = category.DisplayName;
            sheet.Cell(row, 9).Value = item.SourceColumn;
            sheet.Cell(row, 10).Value = category.ApiName;
            row++;
        }
    }

    var headerRange = sheet.Range(1, 1, 1, headers.Length);
    headerRange.Style.Font.Bold = true;
    headerRange.Style.Font.FontColor = XLColor.White;
    headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#004B6B");
    sheet.SheetView.FreezeRows(1);
    sheet.RangeUsed()!.CreateTable("LIMProjectParameters");
    sheet.Columns().AdjustToContents(10, 55);
    sheet.Column(3).Width = 55;
    sheet.Column(9).Width = 55;

    var instructions = workbook.AddWorksheet("Instructions");
    var guidance = new[]
    {
        "LIM Landscape Project Parameter Import",
        "1. In Revit, run WWPTools > Setup > Add Project Parameter > Import from Excel.",
        $"2. Select this workbook and the companion shared-parameter file: {Path.GetFileName(sharedPath)}.",
        "3. Keep the worksheet name 'Project Parameters'; the importer reads that name explicitly.",
        "4. All rows are Type parameters. Environmental/classification fields bind to Planting and Floors; maximum height/width bind to Planting.",
        "5. Reopen LIM, load the mapper catalogs, and use Auto-map all.",
        "Note: Label_* names found in the Dynamo graphs are annotation-family parameters and are intentionally excluded from this project-parameter import.",
        "Note: WWP_SIT_* names in the Dynamo graphs are Revit family/type names, not parameter definitions."
    };
    for (var index = 0; index < guidance.Length; index++)
    {
        instructions.Cell(index + 1, 1).Value = guidance[index];
    }

    instructions.Cell(1, 1).Style.Font.Bold = true;
    instructions.Cell(1, 1).Style.Font.FontSize = 16;
    instructions.Column(1).Width = 120;
    instructions.Column(1).Style.Alignment.WrapText = true;
    workbook.SaveAs(path);
}

string CreateStableGuid(string parameterName)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{sharedGroup}|{parameterName}"));
    var bytes = hash[..16];
    bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
    bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
    return new Guid(bytes).ToString("D");
}

internal sealed record CategoryBinding(string DisplayName, string ApiName);

internal sealed record Definition(
    string Name,
    string SharedDataType,
    string RevitTypeLabel,
    string Description,
    string SourceColumn,
    IReadOnlyList<CategoryBinding> Categories);
