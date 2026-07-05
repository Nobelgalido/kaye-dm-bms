using KayeDM.Application.Dashboard;
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Dashboard;

public class DashboardService : IDashboardService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public DashboardService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<DashboardKpiDto> GetKpisAsync(DateOnly from, DateOnly to)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var fromDate = from.ToDateTime(TimeOnly.MinValue);
        var toDate = to.ToDateTime(TimeOnly.MinValue).AddDays(1);

        // Project only the columns this method actually uses — never pull
        // whole Order entities just to sum/count a handful of fields.
        var completedOrders = await db.Orders
            .Where(o => o.CreatedAt >= fromDate && o.CreatedAt < toDate && o.Status == OrderStatus.Completed)
            .Select(o => new { o.AmountTendered, o.ChangeGiven, o.IsCrewMeal })
            .ToListAsync();

        var totalSales = completedOrders.Sum(o => o.AmountTendered - o.ChangeGiven);

        // Materialize the narrow projection, then sum client-side. This is
        // forced by a SQLite test-provider limitation (it can't translate
        // SUM over decimal into SQL) — not a stylistic choice.
        var expenseAmounts = await db.Expenses
            .Where(e => e.Date >= fromDate && e.Date < toDate)
            .Select(e => e.Amount)
            .ToListAsync();
        var totalExpenses = expenseAmounts.Sum();

        var orderCount = completedOrders.Count;
        var averageTicket = orderCount == 0 ? 0m : totalSales / orderCount;
        var crewMealsGiven = completedOrders.Count(o => o.IsCrewMeal);

        var nonCrewLinePrices = await db.OrderLines
            .Where(l => l.Order.CreatedAt >= fromDate && l.Order.CreatedAt < toDate
                && l.Order.Status == OrderStatus.Completed && !l.Order.IsCrewMeal)
            .Select(l => l.UnitPriceAtSale)
            .ToListAsync();
        var avgLinePrice = nonCrewLinePrices.Count == 0 ? 0m : nonCrewLinePrices.Average();
        var crewMealsEstimatedCost = avgLinePrice * crewMealsGiven;

        return new DashboardKpiDto(totalSales, totalExpenses, totalSales - totalExpenses, orderCount, averageTicket, crewMealsGiven, crewMealsEstimatedCost);
    }

    public async Task<SalesByHourResult> GetSalesByHourAsync(DateOnly date)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);

        var orders = await db.Orders
            .Where(o => o.CreatedAt >= dayStart && o.CreatedAt < dayEnd && o.Status == OrderStatus.Completed)
            .Select(o => new { o.CreatedAt, Sales = o.AmountTendered - o.ChangeGiven })
            .ToListAsync();

        var hours = Enumerable.Range(0, 24)
            .Select(h => new HourlySalesPoint(
                h,
                orders.Where(o => o.CreatedAt.Hour == h).Sum(o => o.Sales),
                orders.Count(o => o.CreatedAt.Hour == h)))
            .ToList();

        // No .Include() needed — projecting t.BusCompany.Name directly in
        // Select translates to a SQL join and pulls only the columns named
        // in the DTO constructor, not the whole BusTrip/BusCompany rows.
        var arrivals = await db.BusTrips
            .AsNoTracking()
            .Where(t => t.ArrivedAt >= dayStart && t.ArrivedAt < dayEnd)
            .Select(t => new BusArrivalMarker(t.ArrivedAt, t.BusCompany.Name, t.BusNumber))
            .ToListAsync();

        return new SalesByHourResult(date, hours, arrivals);
    }

    public async Task<List<DailyTrendPoint>> GetRevenueExpenseTrendAsync(DateOnly from, DateOnly to)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var points = new List<DailyTrendPoint>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var dayStart = day.ToDateTime(TimeOnly.MinValue);
            var dayEnd = dayStart.AddDays(1);

            // Materialize then sum client-side — the SQLite EF Core provider
            // (used by tests) can't translate SUM over decimal into SQL.
            var dayOrderTotals = await db.Orders
                .Where(o => o.CreatedAt >= dayStart && o.CreatedAt < dayEnd && o.Status == OrderStatus.Completed)
                .Select(o => o.AmountTendered - o.ChangeGiven)
                .ToListAsync();
            var revenue = dayOrderTotals.Sum();

            var dayExpenseAmounts = await db.Expenses
                .Where(e => e.Date >= dayStart && e.Date < dayEnd)
                .Select(e => e.Amount)
                .ToListAsync();
            var expenses = dayExpenseAmounts.Sum();

            points.Add(new DailyTrendPoint(day, revenue, expenses, revenue - expenses));
        }

        return points;
    }

    public async Task<List<ExpenseCategoryBreakdownRow>> GetExpenseBreakdownAsync(int year, int month)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);

        // Narrow SQL projection first (Where + Select of only CategoryName
        // and Amount, no whole entities) — then group and sum client-side.
        // GroupBy+SUM over decimal can't be translated to SQL by the SQLite
        // test provider, so the aggregation itself must happen after
        // materializing, not as a stylistic choice.
        var rows = await db.Expenses
            .AsNoTracking()
            .Where(e => e.Date >= monthStart && e.Date < monthEnd)
            .Select(e => new { CategoryName = e.ExpenseCategory.Name, e.Amount })
            .ToListAsync();

        return rows
            .GroupBy(r => r.CategoryName)
            .Select(g => new ExpenseCategoryBreakdownRow(g.Key, g.Sum(r => r.Amount)))
            .OrderByDescending(r => r.Amount)
            .ToList();
    }

    public Task<List<TopDishRow>> GetTopDishesAsync(int days) => throw new NotImplementedException();
    public Task<List<WasteByDishRow>> GetWasteByDishAsync(int days) => throw new NotImplementedException();
    public Task<List<BusCompanySalesRow>> GetSalesPerBusCompanyAsync(DateOnly from, DateOnly to) => throw new NotImplementedException();
    public Task<List<PaymentMethodSplitRow>> GetPaymentMethodSplitAsync(DateOnly from, DateOnly to) => throw new NotImplementedException();
    public Task<List<InsightCallout>> GetInsightsAsync(DateOnly from, DateOnly to) => throw new NotImplementedException();
}
