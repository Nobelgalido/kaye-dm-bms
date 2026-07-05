namespace KayeDM.Application.Dashboard;

public interface IDashboardService
{
    Task<DashboardKpiDto> GetKpisAsync(DateOnly from, DateOnly to);
    Task<SalesByHourResult> GetSalesByHourAsync(DateOnly date);
    Task<List<DailyTrendPoint>> GetRevenueExpenseTrendAsync(DateOnly from, DateOnly to);
    Task<List<ExpenseCategoryBreakdownRow>> GetExpenseBreakdownAsync(int year, int month);
    Task<List<TopDishRow>> GetTopDishesAsync(int days);
    Task<List<WasteByDishRow>> GetWasteByDishAsync(int days);
    Task<List<BusCompanySalesRow>> GetSalesPerBusCompanyAsync(DateOnly from, DateOnly to);
    Task<List<PaymentMethodSplitRow>> GetPaymentMethodSplitAsync(DateOnly from, DateOnly to);
    Task<List<InsightCallout>> GetInsightsAsync(DateOnly from, DateOnly to);
}
