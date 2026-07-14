using KayeDM.Application.Closing;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Closing;

public class ClosingService : IClosingService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public ClosingService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<TodaysFiguresDto> GetTodaysFiguresAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var today = DateTime.Now.Date;
        var tomorrow = today.AddDays(1);

        // Project only the columns this method actually uses — never pull
        // whole Order entities just to sum/count a handful of fields.
        var completedOrders = await db.Orders
            .Where(o => o.CreatedAt >= today && o.CreatedAt < tomorrow && o.Status == OrderStatus.Completed)
            .Select(o => new { o.AmountTendered, o.ChangeGiven, o.PaymentMethod, o.IsCrewMeal })
            .ToListAsync();

        var voidedCount = await db.Orders
            .CountAsync(o => o.CreatedAt >= today && o.CreatedAt < tomorrow && o.Status == OrderStatus.Voided);

        // Materialize the narrow projection, then sum client-side. This is
        // forced by a SQLite test-provider limitation (it can't translate
        // SUM over decimal into SQL) — not a stylistic choice.
        var todaysExpenseAmounts = await db.Expenses
            .Where(e => e.Date >= today && e.Date < tomorrow)
            .Select(e => e.Amount)
            .ToListAsync();
        var totalExpenses = todaysExpenseAmounts.Sum();

        var totalSales = completedOrders.Sum(o => o.AmountTendered - o.ChangeGiven);
        var cashSales = completedOrders.Where(o => o.PaymentMethod == PaymentMethod.Cash).Sum(o => o.AmountTendered - o.ChangeGiven);
        var gcashSales = completedOrders.Where(o => o.PaymentMethod == PaymentMethod.GCash).Sum(o => o.AmountTendered - o.ChangeGiven);
        var crewMealsGiven = completedOrders.Count(o => o.IsCrewMeal);

        var alreadyClosed = await db.DailyClosings.AnyAsync(c => c.Date == today);

        return new TodaysFiguresDto(
            DateOnly.FromDateTime(today),
            totalSales,
            cashSales,
            gcashSales,
            completedOrders.Count,
            voidedCount,
            crewMealsGiven,
            totalExpenses,
            totalSales - totalExpenses,
            alreadyClosed);
    }

    public async Task<DailyClosingDto> CreateClosingAsync(string closedById)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var today = DateTime.Now.Date;
        var figures = await GetTodaysFiguresAsync();

        var entity = new DailyClosing
        {
            Date = today,
            TotalSales = figures.TotalSales,
            CashSales = figures.CashSales,
            GCashSales = figures.GCashSales,
            OrderCount = figures.OrderCount,
            VoidedCount = figures.VoidedCount,
            CrewMealsGiven = figures.CrewMealsGiven,
            TotalExpenses = figures.TotalExpenses,
            NetForDay = figures.NetForDay,
            ClosedById = closedById,
            ClosedAt = DateTime.Now
        };

        db.DailyClosings.Add(entity);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new DomainException($"{today:MMM d, yyyy} has already been closed.");
        }

        return new DailyClosingDto(
            entity.Id, DateOnly.FromDateTime(entity.Date), entity.TotalSales, entity.CashSales, entity.GCashSales,
            entity.OrderCount, entity.VoidedCount, entity.CrewMealsGiven, entity.TotalExpenses, entity.NetForDay,
            entity.ClosedById, entity.ClosedAt);
    }

    public async Task<bool> IsDateClosedAsync(DateOnly date)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var target = date.ToDateTime(TimeOnly.MinValue);
        return await db.DailyClosings.AnyAsync(c => c.Date >= target);
    }

    public async Task<List<DailyClosingDto>> GetHistoryAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        // Materialize entities first, then map client-side — DateOnly.FromDateTime
        // isn't reliably translatable in a Select projection across providers.
        var closings = await db.DailyClosings
            .OrderByDescending(c => c.Date)
            .ToListAsync();

        return closings
            .Select(c => new DailyClosingDto(
                c.Id, DateOnly.FromDateTime(c.Date), c.TotalSales, c.CashSales, c.GCashSales,
                c.OrderCount, c.VoidedCount, c.CrewMealsGiven, c.TotalExpenses, c.NetForDay,
                c.ClosedById, c.ClosedAt))
            .ToList();
    }
}
