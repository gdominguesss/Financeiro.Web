namespace Financeiro.Web.Models;

public sealed class FinanceData
{
    public List<Expense> Expenses { get; set; } = [];
    public List<Income> Incomes { get; set; } = [];
    public CategoryLists Categories { get; set; } = new();
}

public sealed class CategoryLists
{
    public List<string> Expense { get; set; } = [];
    public List<string> Income { get; set; } = [];
}

public sealed class Expense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? RecurringId { get; set; }
    public bool IsRecurring { get; set; }
    public string Month { get; set; } = DateTime.Now.ToString("yyyy-MM");
    public string Description { get; set; } = "";
    public string PaymentMethod { get; set; } = "Crédito";
    public string Installments { get; set; } = "";
    public string ExpenseType { get; set; } = "Essencial";
    public string Category { get; set; } = "Outros";
    public decimal Value { get; set; }
}

public sealed class Income
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? RecurringId { get; set; }
    public bool IsRecurring { get; set; }
    public string Month { get; set; } = DateTime.Now.ToString("yyyy-MM");
    public string Description { get; set; } = "Renda";
    public string Category { get; set; } = "Salário";
    public decimal Amount { get; set; }
}

public sealed record DashboardData(string Month, decimal Income, decimal Expenses, decimal Balance, IReadOnlyList<IncomeSummary> Incomes, IReadOnlyList<CategorySummary> Categories, IReadOnlyList<PaymentSummary> PaymentMethods, IReadOnlyList<TypeSummary> ExpenseTypes);
public sealed record IncomeSummary(string Description, string Category, decimal Amount);
public sealed record CategorySummary(string Category, decimal Value);
public sealed record PaymentSummary(string PaymentMethod, decimal Value);
public sealed record TypeSummary(string ExpenseType, decimal Value);