using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using RecurFolder.Models;

namespace RecurFolder.Services;

public static class PdfExporter
{
    private const double PageWidth = 792;
    private const double PageHeight = 612;
    private const double Margin = 28;
    private const double HeaderHeight = 22;
    private const double RowHeight = 16;
    private const double FooterHeight = 22;

    private static readonly XSolidBrush HeaderFill = new(XColor.FromArgb(0x24, 0x30, 0x27));
    private static readonly XSolidBrush HeaderText = new(XColor.FromArgb(0xF6, 0xF3, 0xEE));
    private static readonly XSolidBrush AltFill = new(XColor.FromArgb(0xF6, 0xF3, 0xEE));
    private static readonly XSolidBrush Ink = new(XColor.FromArgb(0x1F, 0x1A, 0x17));
    private static readonly XSolidBrush FolderInk = new(XColor.FromArgb(0x1F, 0x6B, 0x4A));
    private static readonly XSolidBrush Muted = new(XColor.FromArgb(0x6F, 0x67, 0x5F));

    private static readonly string[] Headers = ["Folder name", "Filename", "File path", "Type"];
    private static readonly double[] ColumnWidths = [160, 180, 340, 56];

    private static readonly Lock FontGate = new();
    private static bool _fontsReady;

    public static void Export(string filePath, IReadOnlyList<FileEntry> rows)
    {
        EnsureFonts();

        var headerFont = new XFont("Segoe UI", 8, XFontStyleEx.Bold);
        var cellFont = new XFont("Segoe UI", 8, XFontStyleEx.Regular);

        using var document = new PdfDocument();
        document.Info.Title = "RecurFolder listing";
        document.Info.Author = "RecurFolder";
        document.Info.Creator = "RecurFolder";

        var contentBottom = PageHeight - Margin - FooterHeight;
        XGraphics? graphics = null;
        PdfPage? page = null;
        var pageNumber = 0;
        var y = 0d;

        void StartPage()
        {
            if (graphics is not null)
            {
                DrawFooter(graphics, cellFont, pageNumber);
                graphics.Dispose();
                page?.Close();
            }

            pageNumber++;
            page = document.AddPage();
            page.Width = XUnit.FromPoint(PageWidth);
            page.Height = XUnit.FromPoint(PageHeight);
            graphics = XGraphics.FromPdfPage(page);

            var x = Margin;
            graphics.DrawRectangle(HeaderFill, x, Margin, ColumnWidths.Sum(), HeaderHeight);
            for (var column = 0; column < Headers.Length; column++)
            {
                DrawCell(graphics, Headers[column], headerFont, HeaderText, x, Margin, ColumnWidths[column], HeaderHeight);
                x += ColumnWidths[column];
            }

            y = Margin + HeaderHeight;
        }

        StartPage();

        for (var i = 0; i < rows.Count; i++)
        {
            if (y + RowHeight > contentBottom)
                StartPage();

            var row = rows[i];
            if (i % 2 == 1)
                graphics!.DrawRectangle(AltFill, Margin, y, ColumnWidths.Sum(), RowHeight);

            var values = new[]
            {
                Clean(row.FolderName),
                Clean(row.FileName),
                Clean(row.FilePath),
                Clean(row.Kind)
            };
            var brushes = new[]
            {
                Ink,
                Ink,
                Ink,
                row.Kind == "Folder" ? FolderInk : Ink
            };

            var x = Margin;
            for (var column = 0; column < values.Length; column++)
            {
                DrawCell(graphics!, values[column], cellFont, brushes[column], x, y, ColumnWidths[column], RowHeight);
                x += ColumnWidths[column];
            }

            y += RowHeight;
        }

        DrawFooter(graphics!, cellFont, pageNumber);
        graphics?.Dispose();
        page?.Close();

        document.Save(filePath);
    }

    private static void DrawFooter(XGraphics graphics, XFont font, int pageNumber)
    {
        var text = $"RecurFolder  ·  Page {pageNumber}";
        graphics.DrawString(
            text,
            font,
            Muted,
            new XRect(Margin, PageHeight - Margin - 4, ColumnWidths.Sum(), 14),
            XStringFormats.CenterLeft);
    }

    private static void DrawCell(XGraphics graphics, string text, XFont font, XBrush brush, double x, double y, double width, double height)
    {
        var state = graphics.Save();
        graphics.IntersectClip(new XRect(x, y, width, height));
        graphics.DrawString(text, font, brush, new XRect(x + 6, y, Math.Max(0, width - 10), height), XStringFormats.CenterLeft);
        graphics.Restore(state);
    }

    private static string Clean(string value)
    {
        if (value.Length == 0)
            return value;

        var buffer = value.ToCharArray();
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] is '\r' or '\n' or '\t' || char.IsControl(buffer[i]))
                buffer[i] = ' ';
        }

        return new string(buffer);
    }

    private static void EnsureFonts()
    {
        lock (FontGate)
        {
            if (_fontsReady)
                return;

            GlobalFontSettings.FontResolver = new SegoeFontResolver();
            _fontsReady = true;
        }
    }

    private sealed class SegoeFontResolver : IFontResolver
    {
        private readonly Dictionary<string, byte[]> _faces = new(StringComparer.OrdinalIgnoreCase);

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            var preferred = isBold ? "segoeuib.ttf" : "segoeui.ttf";
            var fallback = isBold ? "arialbd.ttf" : "arial.ttf";
            var path = Path.Combine(fonts, preferred);
            if (!File.Exists(path))
                path = Path.Combine(fonts, fallback);

            if (!_faces.ContainsKey(path))
                _faces[path] = File.ReadAllBytes(path);

            return new FontResolverInfo(path);
        }

        public byte[]? GetFont(string faceName) =>
            _faces.TryGetValue(faceName, out var bytes) ? bytes : null;
    }
}
