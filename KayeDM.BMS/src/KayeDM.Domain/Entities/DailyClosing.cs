namespace KayeDM.Domain.Entities;

public class DailyClosing
{
    public int Id { get; set; }

    // Date-only in practice — always stored/queried at midnight, one row per calendar day.
    public DateTime Date { get; set; }

    public decimal TotalSales { get; set; }
    public decimal CashSales { get; set; }
    public decimal GCashSales { get; set; }
    public int OrderCount { get; set; }
    public int VoidedCount { get; set; }
    public int CrewMealsGiven { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetForDay { get; set; }

    public string ClosedById { get; set; } = string.Empty;
    public DateTime ClosedAt { get; set; }
}
