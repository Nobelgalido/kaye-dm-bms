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

    public async Task<List<TopDishRow>> GetTopDishesAsync(int days)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var since = DateTime.Now.Date.AddDays(-(days - 1));

        // Narrow SQL projection first (Where + Select of only the columns
        // used below, no whole entities) — then group and sum client-side.
        // GroupBy+SUM over decimal (UnitPriceAtSale * Quantity) can't be
        // translated to SQL by the SQLite test provider, so the aggregation
        // itself must happen after materializing, not as a stylistic choice.
        var rows = await db.OrderLines
            .AsNoTracking()
            .Where(l => l.Order.CreatedAt >= since && l.Order.Status == OrderStatus.Completed)
            .Select(l => new { l.MenuItemId, MenuItemName = l.MenuItem.Name, l.UnitPriceAtSale, l.Quantity })
            .ToListAsync();

        return rows
            .GroupBy(r => new { r.MenuItemId, r.MenuItemName })
            .Select(g => new TopDishRow(g.Key.MenuItemId, g.Key.MenuItemName, g.Sum(r => r.UnitPriceAtSale * r.Quantity), g.Sum(r => r.Quantity)))
            .OrderByDescending(r => r.Revenue)
            .ToList();
    }

    public async Task<List<WasteByDishRow>> GetWasteByDishAsync(int days)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var since = DateTime.Now.Date.AddDays(-(days - 1));

        // Narrow projections — only the columns actually used below.
        var batches = await db.DishBatches
            .AsNoTracking()
            .Where(b => b.Date >= since)
            .Select(b => new { b.Id, b.MenuItemId, MenuItemName = b.MenuItem.Name, b.TraysProduced, b.ServingsPerTray })
            .ToListAsync();

        var wasteByBatch = await db.WasteLogs
            .AsNoTracking()
            .Where(w => w.DishBatch.Date >= since)
            .Select(w => new { w.DishBatchId, w.TraysWasted })
            .ToListAsync();

        var rows = new List<WasteByDishRow>();
        foreach (var group in batches.GroupBy(b => new { b.MenuItemId, b.MenuItemName }))
        {
            var produced = (int)group.Sum(b => b.TraysProduced * b.ServingsPerTray);
            var batchIds = group.Select(b => b.Id).ToHashSet();
            var wastedTrays = wasteByBatch.Where(w => batchIds.Contains(w.DishBatchId)).Sum(w => w.TraysWasted);
            var servingsPerTray = group.First().ServingsPerTray;
            var wasted = (int)(wastedTrays * servingsPerTray);
            var wastePercent = produced == 0 ? 0m : Math.Round((decimal)wasted / produced * 100m, 1);

            rows.Add(new WasteByDishRow(group.Key.MenuItemId, group.Key.MenuItemName, produced, wasted, wastePercent));
        }

        return rows.OrderByDescending(r => r.WastePercent).ToList();
    }

    public async Task<List<BusCompanySalesRow>> GetSalesPerBusCompanyAsync(DateOnly from, DateOnly to)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var fromDate = from.ToDateTime(TimeOnly.MinValue);
        var toDate = to.ToDateTime(TimeOnly.MinValue).AddDays(1);

        // Narrow projections — only the columns actually used below.
        var companies = await db.BusCompanies.AsNoTracking()
            .Select(c => new { c.Id, c.Name })
            .ToListAsync();
        var trips = await db.BusTrips
            .AsNoTracking()
            .Where(t => t.ArrivedAt >= fromDate && t.ArrivedAt < toDate)
            .Select(t => new { t.Id, t.BusCompanyId, t.ArrivedAt })
            .ToListAsync();
        var completedOrders = await db.Orders
            .AsNoTracking()
            .Where(o => o.CreatedAt >= fromDate && o.CreatedAt < toDate && o.Status == OrderStatus.Completed)
            .Select(o => new { o.BusTripId, o.CreatedAt, Sales = o.AmountTendered - o.ChangeGiven })
            .ToListAsync();

        var rows = new List<BusCompanySalesRow>();
        foreach (var company in companies)
        {
            var companyTrips = trips.Where(t => t.BusCompanyId == company.Id).ToList();
            var tripIds = companyTrips.Select(t => t.Id).ToHashSet();

            var directOrders = completedOrders.Where(o => o.BusTripId is not null && tripIds.Contains(o.BusTripId.Value)).ToList();

            // Wave-attributed: unassigned orders completed within +/-20 minutes
            // of any of this company's arrivals. A heuristic (see DTO comment)
            // -- intentionally not mutually exclusive with other companies.
            var waveOrders = completedOrders
                .Where(o => o.BusTripId is null
                    && companyTrips.Any(t => Math.Abs((o.CreatedAt - t.ArrivedAt).TotalMinutes) <= 20))
                .ToList();

            rows.Add(new BusCompanySalesRow(
                company.Id, company.Name,
                directOrders.Sum(o => o.Sales), directOrders.Count,
                waveOrders.Sum(o => o.Sales), waveOrders.Count));
        }

        return rows.OrderByDescending(r => r.DirectSales + r.WaveAttributedSales).ToList();
    }

    public async Task<List<PaymentMethodSplitRow>> GetPaymentMethodSplitAsync(DateOnly from, DateOnly to)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var fromDate = from.ToDateTime(TimeOnly.MinValue);
        var toDate = to.ToDateTime(TimeOnly.MinValue).AddDays(1);

        // Narrow SQL projection first — then group and sum client-side.
        // GroupBy+SUM over decimal can't be translated to SQL by the SQLite
        // test provider, so the aggregation itself must happen after
        // materializing, not as a stylistic choice.
        var rows = await db.Orders
            .AsNoTracking()
            .Where(o => o.CreatedAt >= fromDate && o.CreatedAt < toDate && o.Status == OrderStatus.Completed)
            .Select(o => new { o.PaymentMethod, o.AmountTendered, o.ChangeGiven })
            .ToListAsync();

        return rows
            .GroupBy(r => r.PaymentMethod)
            .Select(g => new PaymentMethodSplitRow(g.Key.ToString(), g.Sum(r => r.AmountTendered - r.ChangeGiven), g.Count()))
            .ToList();
    }

    public async Task<List<InsightCallout>> GetInsightsAsync(DateOnly from, DateOnly to)
    {
        var insights = new List<InsightCallout>();

        var trend = await GetRevenueExpenseTrendAsync(from, to);
        if (trend.Count > 0)
        {
            var best = trend.OrderByDescending(t => t.Net).First();
            var worst = trend.OrderBy(t => t.Net).First();
            insights.Add(new InsightCallout("Best day", $"{best.Date:MMM d} had the best net for the period at {best.Net:C}."));
            if (worst.Net < best.Net)
            {
                insights.Add(new InsightCallout("Worst day", $"{worst.Date:MMM d} had the worst net for the period at {worst.Net:C}."));
            }
        }

        var days = to.DayNumber - from.DayNumber + 1;
        var waste = await GetWasteByDishAsync(days);
        var worstWaste = waste.Where(w => w.Produced > 0).OrderByDescending(w => w.WastePercent).FirstOrDefault();
        if (worstWaste is not null && worstWaste.WastePercent > 0)
        {
            insights.Add(new InsightCallout(
                $"{worstWaste.MenuItemName} waste rate {worstWaste.WastePercent}%",
                $"Over the last {days} day(s) — consider producing 1 fewer tray."));
        }

        var busSales = await GetSalesPerBusCompanyAsync(from, to);
        var ranked = busSales
            .Select(b => new { b.CompanyName, TotalOrders = b.DirectOrderCount + b.WaveAttributedOrderCount, TotalSales = b.DirectSales + b.WaveAttributedSales })
            .Where(b => b.TotalOrders > 0)
            .Select(b => new { b.CompanyName, AverageTake = b.TotalSales / b.TotalOrders })
            .ToList();
        if (ranked.Count > 0)
        {
            var overallAverage = ranked.Average(r => r.AverageTake);
            var best = ranked.OrderByDescending(r => r.AverageTake).First();
            if (overallAverage > 0 && best.AverageTake > overallAverage)
            {
                var pctAbove = Math.Round((best.AverageTake - overallAverage) / overallAverage * 100m, 0);
                insights.Add(new InsightCallout(
                    $"{best.CompanyName} averages {best.AverageTake:C} per order",
                    $"{pctAbove}% above the range average."));
            }
        }

        return insights;
    }
}
