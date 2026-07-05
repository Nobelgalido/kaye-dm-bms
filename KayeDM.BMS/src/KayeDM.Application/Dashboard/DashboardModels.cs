namespace KayeDM.Application.Dashboard;

public record DashboardKpiDto(
    decimal TotalSales,
    decimal TotalExpenses,
    decimal NetProfit,
    int OrderCount,
    decimal AverageTicket,
    int CrewMealsGiven,
    decimal CrewMealsEstimatedCost);

public record HourlySalesPoint(int Hour, decimal Sales, int OrderCount);

public record BusArrivalMarker(DateTime ArrivedAt, string CompanyName, string BusNumber);

public record SalesByHourResult(DateOnly Date, IReadOnlyList<HourlySalesPoint> Hours, IReadOnlyList<BusArrivalMarker> Arrivals);

public record DailyTrendPoint(DateOnly Date, decimal Revenue, decimal Expenses, decimal Net);

public record ExpenseCategoryBreakdownRow(string CategoryName, decimal Amount);

public record TopDishRow(int MenuItemId, string MenuItemName, decimal Revenue, int QuantitySold);

public record WasteByDishRow(int MenuItemId, string MenuItemName, int Produced, int Wasted, decimal WastePercent);

// DirectSales/Count = orders explicitly assigned to one of this company's trips
// via the POS "assign to bus" dropdown. WaveAttributed* = unassigned orders
// completed within +/-20 minutes of any of this company's arrivals -- a
// heuristic, labeled as such in the UI, and can double-count an order across
// companies when waves overlap (multiple buses arriving together).
public record BusCompanySalesRow(
    int BusCompanyId,
    string CompanyName,
    decimal DirectSales,
    int DirectOrderCount,
    decimal WaveAttributedSales,
    int WaveAttributedOrderCount);

public record PaymentMethodSplitRow(string PaymentMethod, decimal Amount, int OrderCount);

public record InsightCallout(string Title, string Detail);

public record DashboardInsightsRequest(DateOnly From, DateOnly To);
