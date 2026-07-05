using KayeDM.Domain.Enums;

namespace KayeDM.Application.Expenses;

public record ExpenseCategoryDto(int Id, string Name, ExpenseCategoryType Type, bool IsActive);

public record ExpenseCategoryUpsertDto(string Name, ExpenseCategoryType Type);

public record CreateExpenseRequest(
    DateTime Date,
    int ExpenseCategoryId,
    string Description,
    decimal Amount,
    ExpensePaymentMethod PaymentMethod,
    string? Vendor,
    string? ReceiptRef,
    string LoggedById);

public record UpdateExpenseRequest(
    DateTime Date,
    int ExpenseCategoryId,
    string Description,
    decimal Amount,
    ExpensePaymentMethod PaymentMethod,
    string? Vendor,
    string? ReceiptRef);

public record ExpenseDto(
    int Id,
    DateTime Date,
    int ExpenseCategoryId,
    string CategoryName,
    string Description,
    decimal Amount,
    ExpensePaymentMethod PaymentMethod,
    string? Vendor,
    string? ReceiptRef,
    DateTime LoggedAt);

// AmountsByMonth is keyed by "yyyy-MM" — Months carries the same keys in
// display order so the page never needs to sort a Dictionary itself.
public record ExpenseMonthlySummaryRow(string CategoryName, IReadOnlyDictionary<string, decimal> AmountsByMonth, decimal Total);

public record ExpenseMonthlySummaryResult(
    IReadOnlyList<string> Months,
    IReadOnlyList<ExpenseMonthlySummaryRow> Rows,
    IReadOnlyDictionary<string, decimal> TotalsByMonth,
    decimal GrandTotal);
