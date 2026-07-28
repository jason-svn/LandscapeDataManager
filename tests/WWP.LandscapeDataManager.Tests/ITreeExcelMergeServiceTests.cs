using ClosedXML.Excel;
using WWP.LandscapeDataManager.App.Services;
using Xunit;

namespace WWP.LandscapeDataManager.Tests;

public sealed class ITreeExcelMergeServiceTests
{
    [Fact]
    public async Task Merge_replaces_zero_byte_file_created_by_the_Windows_save_picker()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"itree-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "new-catalog.xlsx");
            File.WriteAllBytes(path, []);
            var records = new List<ITreeExportRecord>
            {
                new("QURO", new Dictionary<string, object?>
                {
                    ["Species_Code"] = "QURO",
                    ["Scientific_Name"] = "Quercus robur",
                    ["Common_Name"] = "English oak"
                })
            };

            var result = await new ITreeExcelMergeService().MergeAsync(
                records,
                new ITreeExcelMergeOptions(path, "iTree Data", 1, true, true));

            Assert.Equal(1, result.AppendedRows);
            Assert.Null(result.BackupPath);
            using var workbook = new XLWorkbook(path);
            var worksheet = workbook.Worksheet("iTree Data");
            Assert.Equal("QURO", worksheet.Cell("A2").GetString());
            Assert.Equal("Quercus robur", worksheet.Cell("B2").GetString());
            Assert.Equal("English oak", worksheet.Cell("C2").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Merge_updates_matches_and_preserves_custom_content_and_unmatched_rows()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"itree-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "existing.xlsx");
            using (var source = new XLWorkbook())
            {
                var sheet = source.AddWorksheet("Trees");
                sheet.Cell("A1").Value = "Species_Code";
                sheet.Cell("B1").Value = "Annual_Benefit_USD";
                sheet.Cell("C1").Value = "Custom_Note";
                sheet.Cell("D1").Value = "Custom_Formula";
                sheet.Cell("A2").Value = "QURO";
                sheet.Cell("B2").Value = 1d;
                sheet.Cell("C2").Value = "keep this";
                sheet.Cell("D2").FormulaA1 = "LEN(C2)";
                sheet.Cell("A3").Value = "PRIVATE1";
                sheet.Cell("B3").Value = 99d;
                sheet.Cell("C3").Value = "unmatched row";
                source.SaveAs(path);
            }

            var records = new List<ITreeExportRecord>
            {
                new("QURO", new Dictionary<string, object?>
                {
                    ["Species_Code"] = "QURO",
                    ["Annual_Benefit_USD"] = 42.5d,
                    ["Annual_RunoffAvoided_gal"] = 12.25d
                }),
                new("PIMA", new Dictionary<string, object?>
                {
                    ["Species_Code"] = "PIMA",
                    ["Annual_Benefit_USD"] = 8d,
                    ["Annual_RunoffAvoided_gal"] = 3d
                })
            };

            var result = await new ITreeExcelMergeService().MergeAsync(
                records,
                new ITreeExcelMergeOptions(path, "Trees", 1, true, true));

            Assert.Equal(1, result.UpdatedRows);
            Assert.Equal(1, result.AppendedRows);
            Assert.Equal(1, result.PreservedUnmatchedRows);
            Assert.Equal(1, result.AddedColumns);
            Assert.NotNull(result.BackupPath);
            Assert.True(File.Exists(result.BackupPath));

            using var merged = new XLWorkbook(path);
            var worksheet = merged.Worksheet("Trees");
            Assert.Equal(42.5d, worksheet.Cell("B2").GetDouble());
            Assert.Equal("keep this", worksheet.Cell("C2").GetString());
            Assert.Equal("LEN(C2)", worksheet.Cell("D2").FormulaA1);
            Assert.Equal("PRIVATE1", worksheet.Cell("A3").GetString());
            Assert.Equal(99d, worksheet.Cell("B3").GetDouble());
            Assert.Equal("unmatched row", worksheet.Cell("C3").GetString());
            Assert.Equal("Annual_RunoffAvoided_gal", worksheet.Cell("E1").GetString());
            Assert.Equal("PIMA", worksheet.Cell("A4").GetString());
            Assert.Equal(3d, worksheet.Cell("E4").GetDouble());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Merge_updates_all_duplicate_matching_rows_without_touching_custom_columns()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"itree-duplicates-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "duplicates.xlsx");
            using (var source = new XLWorkbook())
            {
                var sheet = source.AddWorksheet("iTree Data");
                sheet.Cell("A1").Value = "Species Code";
                sheet.Cell("B1").Value = "Annual Benefit USD";
                sheet.Cell("C1").Value = "User column";
                sheet.Cell("A2").Value = "quro";
                sheet.Cell("C2").Value = "first";
                sheet.Cell("A3").Value = " QURO ";
                sheet.Cell("C3").Value = "second";
                source.SaveAs(path);
            }

            var records = new List<ITreeExportRecord>
            {
                new("QURO", new Dictionary<string, object?>
                {
                    ["Species_Code"] = "QURO",
                    ["Annual_Benefit_USD"] = 15d
                })
            };
            var result = await new ITreeExcelMergeService().MergeAsync(
                records,
                new ITreeExcelMergeOptions(path, "iTree Data", 1, true, false));

            Assert.Equal(2, result.UpdatedRows);
            using var merged = new XLWorkbook(path);
            var worksheet = merged.Worksheet("iTree Data");
            Assert.Equal(15d, worksheet.Cell("B2").GetDouble());
            Assert.Equal(15d, worksheet.Cell("B3").GetDouble());
            Assert.Equal("first", worksheet.Cell("C2").GetString());
            Assert.Equal("second", worksheet.Cell("C3").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
