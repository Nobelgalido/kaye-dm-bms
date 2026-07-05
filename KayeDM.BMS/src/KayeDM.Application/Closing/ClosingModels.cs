namespace KayeDM.Application.Closing;

public record TodaysFiguresDto(
    DateOnly Date,
    decimal TotalSales,
    decimal CashSales,
    decimal GCashSales,
    int OrderCount,
    int VoidedCount,
    int CrewMealsGiven,
    decimal TotalExpenses,
    decimal NetForDay,
    bool AlreadyClosed);

public record DailyClosingDto(
    int Id,
    DateOnly Date,
    decimal TotalSales,
    decimal CashSales,
    decimal GCashSales,
    int OrderCount,
    int VoidedCount,
    int CrewMealsGiven,
    decimal TotalExpenses,
    decimal NetForDay,
    string ClosedById,
    DateTime ClosedAt);
