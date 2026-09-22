using System.Text.Json;
using Financeiro.Web.Models;

namespace Financeiro.Web.Services;

public sealed class FinanceStore
{
    private static readonly string[] DefaultExpenseCategories = ["Entretenimento", "Alimentação", "Moradia", "Seguro", "Empréstimos", "Animais de estimação", "Impostos", "Transporte", "Educação", "Inglês", "Investimentos", "Guardar dinheiro", "Presentes", "Amazon Prime", "Tim", "Luz", "Mercado", "Açougue", "Vale Refeição", "Spotify", "Mensalidades", "Compras", "Internet + TV + Globoplay + Netflix", "Carro", "Mãe mercado", "Seguro cartão", "iCloud", "Cuidados pessoais", "Corte de cabelo", "PS Plus", "Academia", "Whey + Creatina"];
    private static readonly string[] DefaultIncomeCategories = ["Salário", "Renda extra", "Investimentos", "Freelance", "Outros"];
    private readonly string _dataPath;
    private readonly WorkbookImporter _importer;
    private readonly object _sync = new();
    private FinanceData _data;

    public FinanceStore(IWebHostEnvironment environment, WorkbookImporter importer)
    {
        _dataPath = Path.Combine(environment.ContentRootPath, "App_Data", "finance-data.json");
        _importer = importer;
        Directory.CreateDirectory(Path.GetDirectoryName(_dataPath)!);
        _data = Load();
        EnsureCategories();
        if (_data.Expenses.Count == 0)
        {
            var workbook = Path.Combine(environment.ContentRootPath, "Orçamento mensal.xlsx");
            if (File.Exists(workbook))
            {
                using var stream = File.OpenRead(workbook);
                _data = _importer.Import(stream);
                Persist();
            }
        }

        if (_data.Incomes.Count == 0)
        {
            var workbook = Path.Combine(environment.ContentRootPath, "Orçamento mensal.xlsx");
            if (File.Exists(workbook))
            {
                using var stream = File.OpenRead(workbook);
                var imported = _importer.Import(stream);
                if (imported.Incomes.Count > 0)
                {
                    _data.Incomes = imported.Incomes;
                    Persist();
                }
            }
        }

        EnsureCategories();
    }

    public CategoryLists GetCategories() => _data.Categories;

    public Task<CategoryLists> SaveCategoryAsync(string type, string name, string? originalName = null)
    {
        lock (_sync)
        {
            var list = GetCategoryList(type);
            var cleanName = name.Trim();
            if (string.IsNullOrWhiteSpace(cleanName)) throw new ArgumentException("A categoria não pode ficar vazia.");
            if (!string.IsNullOrWhiteSpace(originalName))
            {
                var index = list.FindIndex(x => x == originalName);
                if (index >= 0) list[index] = cleanName;
                UpdateCategoryReferences(type, originalName, cleanName);
            }
            else if (!list.Contains(cleanName, StringComparer.OrdinalIgnoreCase)) list.Add(cleanName);
            Persist();
            return Task.FromResult(_data.Categories);
        }
    }

    public Task<CategoryLists> DeleteCategoryAsync(string type, string name)
    {
        lock (_sync)
        {
            GetCategoryList(type).RemoveAll(x => x == name);
            Persist();
            return Task.FromResult(_data.Categories);
        }
    }

    public DashboardData GetDashboard(string? month)
    {
        var selectedMonth = NormalizeMonth(month);
        var expenses = GetExpenses(selectedMonth, null, null, null);
        var income = _data.Incomes.Where(x => x.Month == selectedMonth).Sum(x => x.Amount);
        var totalExpenses = expenses.Sum(x => x.Value);
        return new DashboardData(selectedMonth, income, totalExpenses, income - totalExpenses,
            GetIncomes(selectedMonth, null).GroupBy(x => x.Category).Select(g => new IncomeSummary(g.First().Description, g.Key, g.Sum(x => x.Amount))).OrderByDescending(x => x.Amount).ToList(),
            expenses.GroupBy(x => x.Category).Select(g => new CategorySummary(g.Key, g.Sum(x => x.Value))).OrderByDescending(x => x.Value).ToList(),
            expenses.GroupBy(x => x.PaymentMethod).Select(g => new PaymentSummary(g.Key, g.Sum(x => x.Value))).OrderByDescending(x => x.Value).ToList(),
            expenses.GroupBy(x => x.ExpenseType).Select(g => new TypeSummary(g.Key, g.Sum(x => x.Value))).OrderByDescending(x => x.Value).ToList());
    }

    public List<Income> GetIncomes(string? month, string? category)
    {
        var selectedMonth = NormalizeMonth(month);
        lock (_sync)
        {
            return _data.Incomes.Where(x => x.Month == selectedMonth && (string.IsNullOrWhiteSpace(category) || x.Category == category)).ToList();
        }
    }

    public Task<Income> UpsertIncomeAsync(Income income)
    {
        lock (_sync)
        {
            var existing = _data.Incomes.FindIndex(x => x.Id == income.Id);
            if (existing >= 0)
            {
                _data.Incomes[existing] = income;
            }
            else if (income.IsRecurring)
            {
                var recurringId = income.RecurringId ?? Guid.NewGuid();
                for (var offset = 0; offset < 60; offset++)
                {
                    _data.Incomes.Add(new Income
                    {
                        Id = offset == 0 ? income.Id : Guid.NewGuid(),
                        RecurringId = recurringId,
                        IsRecurring = true,
                        Month = AddMonths(income.Month, offset),
                        Description = income.Description,
                        Category = income.Category,
                        Amount = income.Amount
                    });
                }
            }
            else _data.Incomes.Add(income);
            Persist();
            return Task.FromResult(income);
        }
    }

    public Task<bool> DeleteIncomeAsync(Guid id, bool deleteFollowing)
    {
        lock (_sync)
        {
            var target = _data.Incomes.FirstOrDefault(x => x.Id == id);
            if (target is null) return Task.FromResult(false);
            var removed = deleteFollowing && target.RecurringId.HasValue
                ? _data.Incomes.RemoveAll(x => x.RecurringId == target.RecurringId && string.CompareOrdinal(x.Month, target.Month) >= 0) > 0
                : _data.Incomes.RemoveAll(x => x.Id == id) > 0;
            if (removed) Persist();
            return Task.FromResult(removed);
        }
    }

    public List<Expense> GetExpenses(string? month, string? category, string? payment, string? type)
    {
        var selectedMonth = NormalizeMonth(month);
        lock (_sync)
        {
            return _data.Expenses.Where(x => x.Month == selectedMonth && (string.IsNullOrWhiteSpace(category) || x.Category == category) && (string.IsNullOrWhiteSpace(payment) || x.PaymentMethod == payment) && (string.IsNullOrWhiteSpace(type) || x.ExpenseType == type)).ToList();
        }
    }

    public Task<Expense> UpsertExpenseAsync(Expense expense)
    {
        lock (_sync)
        {
            var existing = _data.Expenses.FindIndex(x => x.Id == expense.Id);
            if (existing >= 0)
            {
                _data.Expenses[existing] = expense;
            }
            else if (expense.IsRecurring)
            {
                var recurringId = expense.RecurringId ?? Guid.NewGuid();
                for (var offset = 0; offset < 60; offset++)
                {
                    var occurrence = new Expense
                    {
                        Id = offset == 0 ? expense.Id : Guid.NewGuid(),
                        RecurringId = recurringId,
                        IsRecurring = true,
                        Month = AddMonths(expense.Month, offset),
                        Description = expense.Description,
                        PaymentMethod = expense.PaymentMethod,
                        Installments = expense.Installments,
                        ExpenseType = expense.ExpenseType,
                        Category = expense.Category,
                        Value = expense.Value
                    };
                    _data.Expenses.Add(occurrence);
                }
            }
            else
            {
                _data.Expenses.Add(expense);
            }
            Persist();
            return Task.FromResult(expense);
        }
    }

    public Task<bool> DeleteExpenseAsync(Guid id, bool deleteFollowing)
    {
        lock (_sync)
        {
            var target = _data.Expenses.FirstOrDefault(x => x.Id == id);
            if (target is null) return Task.FromResult(false);
            var removed = deleteFollowing && target.RecurringId.HasValue
                ? _data.Expenses.RemoveAll(x => x.RecurringId == target.RecurringId && string.CompareOrdinal(x.Month, target.Month) >= 0) > 0
                : _data.Expenses.RemoveAll(x => x.Id == id) > 0;
            if (removed) Persist();
            return Task.FromResult(removed);
        }
    }

    public Task ImportAsync(Stream stream)
    {
        var imported = _importer.Import(stream);
        lock (_sync) { imported.Categories = _data.Categories; _data = imported; EnsureCategories(); Persist(); }
        return Task.CompletedTask;
    }

    private FinanceData Load()
    {
        if (!File.Exists(_dataPath)) return new FinanceData();
        var json = File.ReadAllText(_dataPath);
        var data = JsonSerializer.Deserialize<FinanceData>(json) ?? new FinanceData();
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("Expenses", out var expenses))
        {
            for (var index = 0; index < data.Expenses.Count && index < expenses.GetArrayLength(); index++)
            {
                var raw = expenses[index];
                if (data.Expenses[index].Value == 0 && raw.TryGetProperty("ActualCost", out var actual))
                    data.Expenses[index].Value = actual.GetDecimal();
                else if (data.Expenses[index].Value == 0 && raw.TryGetProperty("PlannedCost", out var planned))
                    data.Expenses[index].Value = planned.GetDecimal();
            }
        }
        return data;
    }
    private void EnsureCategories()
    {
        _data.Categories ??= new CategoryLists();
        if (_data.Categories.Expense.Count == 0) _data.Categories.Expense = DefaultExpenseCategories.ToList();
        if (_data.Categories.Income.Count == 0) _data.Categories.Income = DefaultIncomeCategories.ToList();
        Persist();
    }
    private List<string> GetCategoryList(string type) => type.Equals("income", StringComparison.OrdinalIgnoreCase) ? _data.Categories.Income : _data.Categories.Expense;
    private void UpdateCategoryReferences(string type, string originalName, string newName)
    {
        if (type.Equals("income", StringComparison.OrdinalIgnoreCase)) _data.Incomes.Where(x => x.Category == originalName).ToList().ForEach(x => x.Category = newName);
        else _data.Expenses.Where(x => x.Category == originalName).ToList().ForEach(x => x.Category = newName);
    }
    private void Persist() => File.WriteAllText(_dataPath, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
    private static string NormalizeMonth(string? month) => DateTime.TryParse($"{month}-01", out var date) ? date.ToString("yyyy-MM") : DateTime.Now.ToString("yyyy-MM");
    private static string AddMonths(string month, int offset) => DateTime.ParseExact(month, "yyyy-MM", null).AddMonths(offset).ToString("yyyy-MM");
}