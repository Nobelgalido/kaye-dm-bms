using KayeDM.Domain.Enums;

namespace KayeDM.Domain.Entities;

public class Expense
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public int ExpenseCategoryId { get; set; }
    public ExpenseCategory ExpenseCategory { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public ExpensePaymentMethod PaymentMethod { get; set; }
    public string? Vendor { get; set; }
    public string? ReceiptRef { get; set; }

    // TODO Week 4: replace with real Identity user id.
    public string LoggedById { get; set; } = string.Empty;
    public DateTime LoggedAt { get; set; }
}
