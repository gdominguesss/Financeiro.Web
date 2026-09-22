using Financeiro.Web.Models;
using Financeiro.Web.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<WorkbookImporter>();
builder.Services.AddSingleton<FinanceStore>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/categories", (FinanceStore store) => Results.Ok(store.GetCategories()));
app.MapPost("/api/categories/{type}", async (FinanceStore store, string type, HttpRequest request) =>
{
	var payload = await request.ReadFromJsonAsync<CategoryPayload>();
	return payload is null ? Results.BadRequest() : Results.Ok(await store.SaveCategoryAsync(type, payload.Name, payload.OriginalName));
}).DisableAntiforgery();
app.MapDelete("/api/categories/{type}/{name}", async (FinanceStore store, string type, string name) => Results.Ok(await store.DeleteCategoryAsync(type, name)));

app.MapGet("/api/dashboard", (FinanceStore store, string? month) => Results.Ok(store.GetDashboard(month)));
app.MapGet("/api/expenses", (FinanceStore store, string? month, string? category, string? payment, string? type) => Results.Ok(store.GetExpenses(month, category, payment, type)));
app.MapPost("/api/expenses", async (FinanceStore store, HttpRequest request) =>
{
	var expense = await request.ReadFromJsonAsync<Expense>();
	return expense is null ? Results.BadRequest(new { message = "Dados da despesa inválidos." }) : Results.Ok(await store.UpsertExpenseAsync(expense));
}).DisableAntiforgery();
app.MapDelete("/api/expenses/{id:guid}", async (FinanceStore store, Guid id, bool? following) => Results.Ok(await store.DeleteExpenseAsync(id, following == true)));
app.MapGet("/api/incomes", (FinanceStore store, string? month, string? category) => Results.Ok(store.GetIncomes(month, category)));
app.MapPost("/api/incomes", async (FinanceStore store, HttpRequest request) =>
{
	var income = await request.ReadFromJsonAsync<Income>();
	return income is null ? Results.BadRequest(new { message = "Dados do ganho inválidos." }) : Results.Ok(await store.UpsertIncomeAsync(income));
}).DisableAntiforgery();
app.MapDelete("/api/incomes/{id:guid}", async (FinanceStore store, Guid id, bool? following) => Results.Ok(await store.DeleteIncomeAsync(id, following == true)));
app.MapPost("/api/import", async (FinanceStore store, IFormFile file) =>
{
	if (file.Length == 0 || !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
		return Results.BadRequest(new { message = "Envie um arquivo .xlsx válido." });
	await using var stream = file.OpenReadStream();
	await store.ImportAsync(stream);
	return Results.Ok(store.GetDashboard(null));
}).DisableAntiforgery();

app.MapFallbackToFile("index.html");
app.Run();

public sealed record CategoryPayload(string Name, string? OriginalName);
