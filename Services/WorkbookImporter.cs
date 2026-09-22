using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Financeiro.Web.Models;

namespace Financeiro.Web.Services;

public sealed class WorkbookImporter
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public FinanceData Import(Stream source)
    {
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var shared = ReadSharedStrings(archive);
        var rows = ReadSheet(archive, "xl/worksheets/sheet2.xml", shared);
        var data = new FinanceData();
        var report = ReadSheet(archive, "xl/worksheets/sheet1.xml", shared);
        AddIncome(data, report, "D8", "Renda 1", "Salário");
        AddIncome(data, report, "D9", "Renda 2", "Salário");
        AddIncome(data, report, "D10", "Renda extra", "Renda extra");
        foreach (var row in rows.Where(x => x.Key >= 3 && x.Key <= 307))
        {
            var cells = row.Value;
            var description = Value(cells, "B");
            if (string.IsNullOrWhiteSpace(description)) continue;
            data.Expenses.Add(new Expense { Description = description, PaymentMethod = Value(cells, "C"), Installments = Value(cells, "D"), ExpenseType = Value(cells, "E"), Category = Value(cells, "F"), Value = Decimal(cells, "H"), Month = DateTime.Now.ToString("yyyy-MM") });
        }
        return data;
    }

    private static void AddIncome(FinanceData data, Dictionary<int, Dictionary<string, string>> rows, string address, string description, string category)
    {
        var rowNumber = int.Parse(new string(address.SkipWhile(char.IsLetter).ToArray()), CultureInfo.InvariantCulture);
        var column = new string(address.TakeWhile(char.IsLetter).ToArray());
        if (!rows.TryGetValue(rowNumber, out var row) || !row.TryGetValue(column, out var raw) || !decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) || amount == 0) return;
        data.Incomes.Add(new Income { Description = description, Category = category, Amount = amount, Month = DateTime.Now.ToString("yyyy-MM") });
    }

    private static Dictionary<int, Dictionary<string, string>> ReadSheet(ZipArchive archive, string path, IReadOnlyList<string> shared)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"A planilha não contém {path}.");
        using var reader = new StreamReader(entry.Open());
        var doc = XDocument.Load(reader);
        return doc.Descendants(Main + "row").ToDictionary(row => (int)row.Attribute("r")!, row => row.Elements(Main + "c").ToDictionary(c => Column(c.Attribute("r")!.Value), c => CellValue(c, shared)));
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        using var reader = new StreamReader(entry.Open());
        return XDocument.Load(reader).Descendants(Main + "si").Select(x => string.Concat(x.Descendants(Main + "t").Select(t => t.Value))).ToList();
    }

    private static string CellValue(XElement cell, IReadOnlyList<string> shared)
    {
        var value = cell.Element(Main + "v")?.Value ?? "";
        return cell.Attribute("t")?.Value == "s" && int.TryParse(value, out var index) && index < shared.Count ? shared[index] : value;
    }

    private static string Column(string address) => new(address.TakeWhile(char.IsLetter).ToArray());
    private static string Value(IReadOnlyDictionary<string, string> cells, string column) => cells.TryGetValue(column, out var value) ? value : "";
    private static decimal Decimal(IReadOnlyDictionary<string, string> cells, string column) => decimal.TryParse(Value(cells, column), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
}