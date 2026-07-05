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

    private static async Task<List<MenuItem>> SeedMenuItemsAsync(AppDbContext db)
    {
        var items = new List<MenuItem>
        {
            new() { Name = "Chicken Adobo", Category = MenuCategory.Ulam, Price = 90m, IsActive = true, SortOrder = 1 },
            new() { Name = "Pork Sinigang", Category = MenuCategory.Ulam, Price = 95m, IsActive = true, SortOrder = 2 },
            new() { Name = "Beef Caldereta", Category = MenuCategory.Ulam, Price = 110m, IsActive = true, SortOrder = 3 },
            new() { Name = "Kare-Kare", Category = MenuCategory.Ulam, Price = 120m, IsActive = true, SortOrder = 4 },
            new() { Name = "Pork Menudo", Category = MenuCategory.Ulam, Price = 85m, IsActive = true, SortOrder = 5 },
            new() { Name = "Dinuguan", Category = MenuCategory.Ulam, Price = 80m, IsActive = true, SortOrder = 6 },
            new() { Name = "Fried Bangus", Category = MenuCategory.Ulam, Price = 90m, IsActive = true, SortOrder = 7 },
            new() { Name = "Fried Chicken", Category = MenuCategory.Ulam, Price = 85m, IsActive = true, SortOrder = 8 },
            new() { Name = "Pork Sisig", Category = MenuCategory.Ulam, Price = 100m, IsActive = true, SortOrder = 9 },
            new() { Name = "Beef Tapa", Category = MenuCategory.Ulam, Price = 95m, IsActive = true, SortOrder = 10 },
            new() { Name = "Plain Rice", Category = MenuCategory.Rice, Price = 15m, IsActive = true, SortOrder = 11 },
            new() { Name = "Garlic Rice", Category = MenuCategory.Rice, Price = 20m, IsActive = true, SortOrder = 12 },
            new() { Name = "Java Rice", Category = MenuCategory.Rice, Price = 25m, IsActive = true, SortOrder = 13 },
            new() { Name = "Iced Tea", Category = MenuCategory.Drinks, Price = 20m, IsActive = true, SortOrder = 14 },
            new() { Name = "Softdrinks", Category = MenuCategory.Drinks, Price = 25m, IsActive = true, SortOrder = 15 },
            new() { Name = "Bottled Water", Category = MenuCategory.Drinks, Price = 20m, IsActive = true, SortOrder = 16 },
            new() { Name = "Buko Juice", Category = MenuCategory.Drinks, Price = 30m, IsActive = true, SortOrder = 17 },
            new() { Name = "Lumpiang Shanghai", Category = MenuCategory.Snacks, Price = 60m, IsActive = true, SortOrder = 18 },
            new() { Name = "Fish Balls", Category = MenuCategory.Snacks, Price = 30m, IsActive = true, SortOrder = 19 },
            new() { Name = "Kwek-Kwek", Category = MenuCategory.Snacks, Price = 35m, IsActive = true, SortOrder = 20 },
            new() { Name = "Banana Cue", Category = MenuCategory.Snacks, Price = 25m, IsActive = true, SortOrder = 21 },
            new() { Name = "Leche Flan", Category = MenuCategory.Dessert, Price = 40m, IsActive = true, SortOrder = 22 },
            new() { Name = "Halo-Halo", Category = MenuCategory.Dessert, Price = 65m, IsActive = true, SortOrder = 23 },
            new() { Name = "Buko Pandan", Category = MenuCategory.Dessert, Price = 45m, IsActive = true, SortOrder = 24 },
            new() { Name = "Turon", Category = MenuCategory.Dessert, Price = 30m, IsActive = true, SortOrder = 25 }
        };

        db.MenuItems.AddRange(items);
        await db.SaveChangesAsync();
        return items;
    }

    private static async Task<List<BusCompany>> SeedBusCompaniesAsync(AppDbContext db)
    {
        var companies = new List<BusCompany>
        {
            new() { Name = "DLTB", ContactPerson = "Juan Dela Cruz", CrewMealAllowancePerTrip = 3, IsActive = true },
            new() { Name = "Isarog", ContactPerson = "Maria Santos", CrewMealAllowancePerTrip = 2, IsActive = true },
            new() { Name = "Peñafrancia Tours", ContactPerson = "Pedro Reyes", CrewMealAllowancePerTrip = 4, IsActive = true },
            new() { Name = "Raymond Transport", ContactPerson = "Ana Villanueva", CrewMealAllowancePerTrip = 3, IsActive = true }
        };

        db.BusCompanies.AddRange(companies);
        await db.SaveChangesAsync();
        return companies;
    }
}
