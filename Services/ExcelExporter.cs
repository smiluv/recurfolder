using ClosedXML.Excel;
using RecurFolder.Models;

namespace RecurFolder.Services;

public static class ExcelExporter
{
    private const int ExcelCellLimit = 32767;

    public static void Export(string filePath, IReadOnlyList<FileEntry> rows)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = "RecurFolder listing";
        workbook.Properties.Author = "RecurFolder";

        var sheet = workbook.Worksheets.Add("Files");
        sheet.Cell(1, 1).Value = "Folder name";
        sheet.Cell(1, 2).Value = "Filename";
        sheet.Cell(1, 3).Value = "File path";
        sheet.Cell(1, 4).Value = "Type";

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = i + 2;
            sheet.Cell(line, 1).Value = Clip(row.FolderName);
            sheet.Cell(line, 2).Value = Clip(row.FileName);
            sheet.Cell(line, 3).Value = Clip(row.FilePath);
            sheet.Cell(line, 4).Value = row.Kind;
        }

        var header = sheet.Range(1, 1, 1, 4);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.FromHtml("#1A1A1A");
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        if (rows.Count > 0)
            sheet.Range(1, 1, rows.Count + 1, 4).SetAutoFilter();

        sheet.SheetView.FreezeRows(1);
        sheet.Column(1).Width = 28;
        sheet.Column(2).Width = 36;
        sheet.Column(3).Width = 88;
        sheet.Column(4).Width = 12;
        sheet.RowHeight = 18;
        sheet.Row(1).Height = 22;

        workbook.SaveAs(filePath);
    }

    private static string Clip(string value) =>
        value.Length <= ExcelCellLimit ? value : value[..ExcelCellLimit];
}
