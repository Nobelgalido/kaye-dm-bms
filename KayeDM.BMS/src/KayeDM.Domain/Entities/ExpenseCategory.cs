using KayeDM.Domain.Enums;

namespace KayeDM.Domain.Entities;

public class ExpenseCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ExpenseCategoryType Type { get; set; }
    public bool IsActive { get; set; } = true;
}
