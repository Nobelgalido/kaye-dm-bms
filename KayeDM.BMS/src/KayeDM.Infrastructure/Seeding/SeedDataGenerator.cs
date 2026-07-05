using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Seeding;

// Fixed seed (42) -- every random draw in this class must go through _random,
// never `new Random()` or `Random.Shared`, or the demo data stops being
// reproducible run-to-run. See blueprint §7.
public class SeedDataGenerator
{
    private readonly Random _random = new(42);
    private readonly DateTime _windowEnd = DateTime.Now.Date;
    private readonly DateTime _windowStart;
    private readonly HashSet<DateTime> _negativeDays;

    public SeedDataGenerator()
    {
        _windowStart = _windowEnd.AddDays(-29);

        // 2-3 designated net-negative days, picked up front so the choice
        // itself is deterministic (drawn from _random in a fixed position in
        // the sequence, before any per-day generation starts).
        var negativeDayCount = _random.Next(2, 4);
        _negativeDays = new HashSet<DateTime>();
        while (_negativeDays.Count < negativeDayCount)
        {
            var offset = _random.Next(0, 30);
            _negativeDays.Add(_windowStart.AddDays(offset));
        }
    }

    public async Task RunAsync(AppDbContext db)
    {
        await WipeAsync(db);

        var menuItems = await SeedMenuItemsAsync(db);
        var busCompanies = await SeedBusCompaniesAsync(db);

        for (var day = _windowStart; day <= _windowEnd; day = day.AddDays(1))
        {
            var trips = await SeedBusTripsForDayAsync(db, day, busCompanies);
            var batches = await SeedDishBatchesForDayAsync(db, day, menuItems);
            await SeedOrdersForDayAsync(db, day, menuItems, trips);
            await SeedWasteForDayAsync(db, batches);
            await SeedExpensesForDayAsync(db, day);

            if (day < _windowEnd)
            {
                await SeedClosingForDayAsync(db, day);
            }
        }
    }

    private static async Task WipeAsync(AppDbContext db)
    {
        // Order matters -- children before parents, respecting FK constraints.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM DailyClosings");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM WasteLogs");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM CrewMealCredits");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM OrderLines");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Orders");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM DishBatches");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM BusTrips");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM BusCompanies");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Expenses");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM MenuItems");
    }
}
