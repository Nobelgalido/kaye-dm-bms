using KayeDM.Application.Expenses;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Closing;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Expenses;

public class ExpenseService : IExpenseService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public ExpenseService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<List<ExpenseCategoryDto>> GetCategoriesAsync(bool includeInactive = false)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var query = db.ExpenseCategories.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query
            .OrderBy(c => c.Name)
            .Select(c => new ExpenseCategoryDto(c.Id, c.Name, c.Type, c.IsActive))
            .ToListAsync();
    }

    public async Task<ExpenseCategoryDto> CreateCategoryAsync(ExpenseCategoryUpsertDto dto)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = new ExpenseCategory { Name = dto.Name, Type = dto.Type, IsActive = true };
        db.ExpenseCategories.Add(entity);
        await db.SaveChangesAsync();

        return new ExpenseCategoryDto(entity.Id, entity.Name, entity.Type, entity.IsActive);
    }

    public async Task<ExpenseCategoryDto> UpdateCategoryAsync(int id, ExpenseCategoryUpsertDto dto)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new DomainException($"Expense category {id} not found.");

        entity.Name = dto.Name;
        entity.Type = dto.Type;
        await db.SaveChangesAsync();

        return new ExpenseCategoryDto(entity.Id, entity.Name, entity.Type, entity.IsActive);
    }

    public async Task SetCategoryActiveAsync(int id, bool isActive)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new DomainException($"Expense category {id} not found.");

        entity.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    public async Task SeedDefaultCategoriesAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        if (await db.ExpenseCategories.AnyAsync())
        {
            return;
        }

        foreach (var type in Enum.GetValues<ExpenseCategoryType>())
        {
            db.ExpenseCategories.Add(new ExpenseCategory { Name = type.ToString(), Type = type, IsActive = true });
        }

        await db.SaveChangesAsync();
    }

    public async Task<ExpenseDto> CreateExpenseAsync(CreateExpenseRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        await ClosingGuard.EnsureDateNotClosedAsync(db, request.Date.Date, "create an expense");

        var category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == request.ExpenseCategoryId)
            ?? throw new DomainException($"Expense category {request.ExpenseCategoryId} not found.");

        if (request.Amount <= 0)
        {
            throw new DomainException("Expense amount must be positive.");
        }

        var entity = new Expense
        {
            Date = request.Date.Date,
            ExpenseCategoryId = request.ExpenseCategoryId,
            Description = request.Description,
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod,
            Vendor = request.Vendor,
            ReceiptRef = request.ReceiptRef,
            LoggedById = request.LoggedById,
            LoggedAt = DateTime.Now
        };

        db.Expenses.Add(entity);
        await db.SaveChangesAsync();

        return new ExpenseDto(entity.Id, entity.Date, entity.ExpenseCategoryId, category.Name, entity.Description, entity.Amount, entity.PaymentMethod, entity.Vendor, entity.ReceiptRef, entity.LoggedAt);
    }

    public async Task<ExpenseDto> UpdateExpenseAsync(int id, UpdateExpenseRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id)
            ?? throw new DomainException($"Expense {id} not found.");

        await ClosingGuard.EnsureDateNotClosedAsync(db, entity.Date, "edit this expense");
        await ClosingGuard.EnsureDateNotClosedAsync(db, request.Date.Date, "edit this expense");

        var category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == request.ExpenseCategoryId)
            ?? throw new DomainException($"Expense category {request.ExpenseCategoryId} not found.");

        if (request.Amount <= 0)
        {
            throw new DomainException("Expense amount must be positive.");
        }

        entity.Date = request.Date.Date;
        entity.ExpenseCategoryId = request.ExpenseCategoryId;
        entity.Description = request.Description;
        entity.Amount = request.Amount;
        entity.PaymentMethod = request.PaymentMethod;
        entity.Vendor = request.Vendor;
        entity.ReceiptRef = request.ReceiptRef;

        await db.SaveChangesAsync();

        return new ExpenseDto(entity.Id, entity.Date, entity.ExpenseCategoryId, category.Name, entity.Description, entity.Amount, entity.PaymentMethod, entity.Vendor, entity.ReceiptRef, entity.LoggedAt);
    }

    public async Task DeleteExpenseAsync(int id)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id)
            ?? throw new DomainException($"Expense {id} not found.");

        await ClosingGuard.EnsureDateNotClosedAsync(db, entity.Date, "delete this expense");

        db.Expenses.Remove(entity);
        await db.SaveChangesAsync();
    }

    public async Task<List<ExpenseDto>> GetExpensesAsync(DateOnly? from, DateOnly? to, int? categoryId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var query = db.Expenses.AsNoTracking().Include(e => e.ExpenseCategory).AsQueryable();

        if (from is not null)
        {
            var fromDate = from.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(e => e.Date >= fromDate);
        }

        if (to is not null)
        {
            var toDate = to.Value.ToDateTime(TimeOnly.MinValue).AddDays(1);
            query = query.Where(e => e.Date < toDate);
        }

        if (categoryId is not null)
        {
            query = query.Where(e => e.ExpenseCategoryId == categoryId);
        }

        return await query
            .OrderByDescending(e => e.Date)
            .Select(e => new ExpenseDto(e.Id, e.Date, e.ExpenseCategoryId, e.ExpenseCategory.Name, e.Description, e.Amount, e.PaymentMethod, e.Vendor, e.ReceiptRef, e.LoggedAt))
            .ToListAsync();
    }

    public async Task<ExpenseMonthlySummaryResult> GetMonthlySummaryAsync(DateOnly from, DateOnly to, int? categoryId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var fromDate = from.ToDateTime(TimeOnly.MinValue);
        var toDate = to.ToDateTime(TimeOnly.MinValue).AddDays(1);

        var query = db.Expenses.AsNoTracking().Include(e => e.ExpenseCategory)
            .Where(e => e.Date >= fromDate && e.Date < toDate);

        if (categoryId is not null)
        {
            query = query.Where(e => e.ExpenseCategoryId == categoryId);
        }

        var expenses = await query.ToListAsync();

        var months = new List<string>();
        for (var month = new DateOnly(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
        {
            months.Add(month.ToString("yyyy-MM"));
        }

        var categories = expenses
            .Select(e => (e.ExpenseCategoryId, e.ExpenseCategory.Name))
            .Distinct()
            .OrderBy(c => c.Name)
            .ToList();

        var rows = new List<ExpenseMonthlySummaryRow>();
        foreach (var (categoryIdValue, categoryName) in categories)
        {
            var amountsByMonth = new Dictionary<string, decimal>();
            foreach (var month in months)
            {
                amountsByMonth[month] = expenses
                    .Where(e => e.ExpenseCategoryId == categoryIdValue && e.Date.ToString("yyyy-MM") == month)
                    .Sum(e => e.Amount);
            }

            rows.Add(new ExpenseMonthlySummaryRow(categoryName, amountsByMonth, amountsByMonth.Values.Sum()));
        }

        var totalsByMonth = months.ToDictionary(month => month, month => rows.Sum(r => r.AmountsByMonth[month]));

        return new ExpenseMonthlySummaryResult(months, rows, totalsByMonth, rows.Sum(r => r.Total));
    }
}
