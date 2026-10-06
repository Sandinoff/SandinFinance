using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SandinFinance.Core;

public static class ExpenseExcelExporter
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static XElement X(string name, params object[] content) => new(Main + name, content);
    private static XAttribute A(string name, object value) => new(name, value);
    private static XElement Text(string cell, string value, int style = 5) => X("c", A("r", cell), A("s", style), A("t", "inlineStr"), X("is", X("t", new XAttribute(XNamespace.Xml + "space", "preserve"), value)));
    private static XElement Number(string cell, object value, int style) => X("c", A("r", cell), A("s", style), X("v", Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0"));

    public static int Export(string filename, IReadOnlyList<Account> accounts, IReadOnlyList<Category> categories)
    {
        if (!string.Equals(Path.GetExtension(filename), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Выберите файл .xlsx.");
        var expenses = BudgetStatistics.Calculate(accounts, categories, CategoryKind.Expense, new(DateOnly.MinValue, DateOnly.MaxValue)).Entries
            .Where(e => !e.IsTransfer && !e.IsInterest).OrderBy(e => e.OccurredAt).ThenBy(e => e.AccountName).ToList();
        if (expenses.Count > 1_048_570) throw new ArgumentException("Слишком много расходов для одного листа Excel.");
        var path = Path.GetFullPath(filename);
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                WriteWorkbook(archive, expenses);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            return expenses.Count;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void WriteWorkbook(ZipArchive archive, IReadOnlyList<BudgetEntry> expenses)
    {
        var last = Math.Max(6, expenses.Count + 5);
        var headers = new[] { "Дата", "Время", "Счёт", "Категория", "Магазин / место", "Сумма, ₽" };
        var total = expenses.Sum(e => e.Amount);
        var rows = new List<XElement>
        {
            X("row", A("r", 1), A("ht", 30), A("customHeight", 1), Text("A1", "Расходы — SandinFinance", 1)),
            X("row", A("r", 2), A("ht", 24), A("customHeight", 1), Text("A2", $"Расходы действующих и скрытых счетов · выгрузка {DateTime.Now:dd.MM.yyyy HH:mm}", 8)),
            X("row", A("r", 3), A("ht", 28), A("customHeight", 1), Text("A3", "Операций", 8), Number("B3", expenses.Count, 0), Text("E3", "Итого", 8), X("c", A("r", "F3"), A("s", 7), X("f", $"SUM(F6:F{last})"), X("v", total.ToString(CultureInfo.InvariantCulture)))),
            X("row", A("r", 4), A("ht", 24), A("customHeight", 1), Text("A4", "Переводы, доходы и начисленные проценты в расходы не включены.", 8)),
            X("row", A("r", 5), A("ht", 26), A("customHeight", 1), headers.Select((h, i) => Text($"{(char)('A' + i)}5", h, 2)))
        };
        for (var index = 0; index < expenses.Count; index++)
        {
            var e = expenses[index]; var row = index + 6;
            var lines = Math.Max(1, Math.Max(Math.Ceiling(e.AccountName.Length / 24d), Math.Max(Math.Ceiling(e.CategoryName.Length / 22d), Math.Ceiling(e.MerchantName.Length / 28d))));
            rows.Add(X("row", A("r", row), A("ht", Math.Max(32, lines * 17 + 8)), A("customHeight", 1), Number($"A{row}", e.OccurredAt.Date.ToOADate(), 3), Number($"B{row}", e.OccurredAt.TimeOfDay.TotalDays, 4),
                Text($"C{row}", e.AccountName), Text($"D{row}", e.CategoryName), Text($"E{row}", e.MerchantName), Number($"F{row}", e.Amount, 6)));
        }
        if (expenses.Count == 0) rows.Add(X("row", A("r", 6)));
        Write(archive, "xl/worksheets/sheet1.xml", X("worksheet", new XAttribute(XNamespace.Xmlns + "r", Relations), X("dimension", A("ref", $"A1:F{last}")),
            X("sheetViews", X("sheetView", A("workbookViewId", 0), A("showGridLines", 0), X("pane", A("ySplit", 5), A("topLeftCell", "A6"), A("activePane", "bottomLeft"), A("state", "frozen")), X("selection", A("pane", "bottomLeft"), A("activeCell", "A6"), A("sqref", "A6")))),
            X("sheetFormatPr", A("defaultRowHeight", 20)), X("cols", new[] { 14, 11, 28, 25, 30, 20 }.Select((width, i) => X("col", A("min", i + 1), A("max", i + 1), A("width", width), A("customWidth", 1)))),
            X("sheetData", rows), X("mergeCells", A("count", 3), X("mergeCell", A("ref", "A1:F1")), X("mergeCell", A("ref", "A2:F2")), X("mergeCell", A("ref", "A4:F4"))),
            X("pageMargins", A("left", 0.3), A("right", 0.3), A("top", 0.5), A("bottom", 0.5), A("header", 0.2), A("footer", 0.2)), X("tableParts", A("count", 1), X("tablePart", new XAttribute(Relations + "id", "rId1")))));
        Write(archive, "xl/tables/table1.xml", X("table", A("id", 1), A("name", "Expenses"), A("displayName", "Expenses"), A("ref", $"A5:F{last}"), A("totalsRowShown", 0),
            X("autoFilter", A("ref", $"A5:F{last}")), X("tableColumns", A("count", 6), headers.Select((h, i) => X("tableColumn", A("id", i + 1), A("name", h)))), X("tableStyleInfo", A("name", "TableStyleMedium4"), A("showFirstColumn", 0), A("showLastColumn", 0), A("showRowStripes", 1), A("showColumnStripes", 0))));
        Write(archive, "xl/workbook.xml", X("workbook", new XAttribute(XNamespace.Xmlns + "r", Relations), X("bookViews", X("workbookView")), X("sheets", X("sheet", A("name", "Расходы"), A("sheetId", 1), new XAttribute(Relations + "id", "rId1"))), X("calcPr", A("calcId", 191029), A("fullCalcOnLoad", 1))));
        XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        XElement Relationship(string id, string type, string target) => new(rel + "Relationship", A("Id", id), A("Type", Relations.NamespaceName + "/" + type), A("Target", target));
        Write(archive, "_rels/.rels", new XElement(rel + "Relationships", Relationship("rId1", "officeDocument", "xl/workbook.xml")));
        Write(archive, "xl/_rels/workbook.xml.rels", new XElement(rel + "Relationships", Relationship("rId1", "worksheet", "worksheets/sheet1.xml"), Relationship("rId2", "styles", "styles.xml")));
        Write(archive, "xl/worksheets/_rels/sheet1.xml.rels", new XElement(rel + "Relationships", Relationship("rId1", "table", "../tables/table1.xml")));
        XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
        XElement Part(string name, string type) => new(ct + "Override", A("PartName", name), A("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml." + type + "+xml"));
        Write(archive, "[Content_Types].xml", new XElement(ct + "Types", new XElement(ct + "Default", A("Extension", "rels"), A("ContentType", "application/vnd.openxmlformats-package.relationships+xml")), new XElement(ct + "Default", A("Extension", "xml"), A("ContentType", "application/xml")),
            Part("/xl/workbook.xml", "sheet.main"), Part("/xl/worksheets/sheet1.xml", "worksheet"), Part("/xl/styles.xml", "styles"), Part("/xl/tables/table1.xml", "table")));
        Write(archive, "xl/styles.xml", XElement.Parse(Styles));
    }

    private static void Write(ZipArchive archive, string name, XElement root)
    {
        using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true });
        new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).Save(writer);
    }
    private const string Styles = """
    <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
      <numFmts count="3"><numFmt numFmtId="164" formatCode="#,##0.00&quot; ₽&quot;"/><numFmt numFmtId="165" formatCode="yyyy-mm-dd"/><numFmt numFmtId="166" formatCode="hh:mm"/></numFmts>
      <fonts count="5"><font><sz val="11"/><color rgb="FF172D35"/><name val="Arial"/></font><font><b/><sz val="11"/><color rgb="FFFFFFFF"/><name val="Arial"/></font><font><b/><sz val="16"/><color rgb="FF172D35"/><name val="Arial"/></font><font><sz val="11"/><color rgb="FF607780"/><name val="Arial"/></font><font><b/><sz val="12"/><color rgb="FF172D35"/><name val="Arial"/></font></fonts>
      <fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF12796B"/><bgColor indexed="64"/></patternFill></fill></fills>
      <borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left/><right/><top/><bottom style="hair"><color rgb="FFE0E7EA"/></bottom><diagonal/></border></borders>
      <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
      <cellXfs count="9"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="2" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyAlignment="1"><alignment vertical="center" wrapText="1"/></xf><xf numFmtId="165" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf><xf numFmtId="166" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf><xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyAlignment="1"><alignment vertical="center" wrapText="1"/></xf><xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf><xf numFmtId="164" fontId="4" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyFont="1"/><xf numFmtId="0" fontId="3" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs>
      <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles><dxfs count="0"/><tableStyles count="0" defaultTableStyle="TableStyleMedium4" defaultPivotStyle="PivotStyleLight16"/>
    </styleSheet>
    """;
}
