namespace KayeDM.Application.Expenses;

public interface IExpenseService
{
    Task<List<ExpenseCategoryDto>> GetCategoriesAsync(bool includeInactive = false);
    Task<ExpenseCategoryDto> CreateCategoryAsync(ExpenseCategoryUpsertDto dto);
    Task<ExpenseCategoryDto> UpdateCategoryAsync(int id, ExpenseCategoryUpsertDto dto);
    Task SetCategoryActiveAsync(int id, bool isActive);

    // Inserts the 7 ExpenseCategoryType defaults if the table is empty. Safe
    // to call on every app start.
    Task SeedDefaultCategoriesAsync();

    Task<ExpenseDto> CreateExpenseAsync(CreateExpenseRequest request);
    Task<ExpenseDto> UpdateExpenseAsync(int id, UpdateExpenseRequest request);
    Task DeleteExpenseAsync(int id);
    Task<List<ExpenseDto>> GetExpensesAsync(DateOnly? from, DateOnly? to, int? categoryId);

    Task<ExpenseMonthlySummaryResult> GetMonthlySummaryAsync(DateOnly from, DateOnly to, int? categoryId);
}
