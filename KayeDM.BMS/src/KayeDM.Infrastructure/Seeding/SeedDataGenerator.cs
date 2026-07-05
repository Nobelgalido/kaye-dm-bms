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

    private static readonly (TimeSpan Start, TimeSpan End)[] Waves =
    {
        (new TimeSpan(9, 30, 0), new TimeSpan(10, 30, 0)),
        (new TimeSpan(13, 30, 0), new TimeSpan(14, 30, 0)),
        (new TimeSpan(18, 0, 0), new TimeSpan(19, 30, 0))
    };

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

    private async Task<List<BusTrip>> SeedBusTripsForDayAsync(AppDbContext db, DateTime day, List<BusCompany> companies)
    {
        var trips = new List<BusTrip>();

        foreach (var (start, end) in Waves)
        {
            var arrivalsThisWave = _random.Next(1, 4); // 1-3 buses per wave
            for (var i = 0; i < arrivalsThisWave; i++)
            {
                var company = companies[_random.Next(companies.Count)];
                var windowMinutes = (int)(end - start).TotalMinutes;
                var arrivedAt = day.Date + start + TimeSpan.FromMinutes(_random.Next(0, windowMinutes));

                var trip = new BusTrip
                {
                    BusCompanyId = company.Id,
                    BusNumber = (8000 + _random.Next(0, 999)).ToString(),
                    Route = "Manila-Sorsogon",
                    ArrivedAt = arrivedAt,
                    DepartedAt = arrivedAt.AddMinutes(20 + _random.Next(0, 15))
                };
                trips.Add(trip);
            }
        }

        db.BusTrips.AddRange(trips);
        await db.SaveChangesAsync();
        return trips;
    }

    private async Task SeedOrdersForDayAsync(AppDbContext db, DateTime day, List<MenuItem> menuItems, List<BusTrip> trips)
    {
        var orderCount = _random.Next(60, 141);
        var sequence = 0;
        var createdOrders = new List<Order>();

        for (var i = 0; i < orderCount; i++)
        {
            DateTime timestamp;
            if (_random.NextDouble() < 0.8 && Waves.Length > 0)
            {
                var (start, end) = Waves[_random.Next(Waves.Length)];
                var center = day.Date + start + TimeSpan.FromTicks((end - start).Ticks / 2);
                var jitterMinutes = _random.Next(-25, 26);
                timestamp = center.AddMinutes(jitterMinutes);
            }
            else
            {
                var minute = _random.Next(8 * 60, 20 * 60); // 8am-8pm trickle
                timestamp = day.Date + TimeSpan.FromMinutes(minute);
            }

            sequence++;
            var orderNumber = $"{day:yyyyMMdd}-{sequence:D3}";
            var isCash = _random.NextDouble() < 0.85;
            var lineCount = _random.Next(1, 5);

            var order = new Order
            {
                OrderNumber = orderNumber,
                CreatedAt = timestamp,
                Status = OrderStatus.Completed,
                PaymentMethod = isCash ? PaymentMethod.Cash : PaymentMethod.GCash
            };

            decimal total = 0m;
            for (var l = 0; l < lineCount; l++)
            {
                var item = menuItems[_random.Next(menuItems.Count)];
                var quantity = _random.Next(1, 4);
                total += item.Price * quantity;
                order.Lines.Add(new OrderLine { MenuItemId = item.Id, Quantity = quantity, UnitPriceAtSale = item.Price });
            }

            var nearbyTrip = trips.FirstOrDefault(t => Math.Abs((timestamp - t.ArrivedAt).TotalMinutes) <= 20);
            if (nearbyTrip is not null && _random.NextDouble() < 0.5)
            {
                order.BusTripId = nearbyTrip.Id;
            }

            if (isCash)
            {
                var overpay = new[] { 0m, 10m, 20m, 50m }[_random.Next(4)];
                order.AmountTendered = total + overpay;
                order.ChangeGiven = overpay;
            }
            else
            {
                order.AmountTendered = total;
                order.ChangeGiven = 0m;
            }

            createdOrders.Add(order);
        }

        db.Orders.AddRange(createdOrders);
        await db.SaveChangesAsync();

        // Occasional voids -- ~4% of the day's completed orders.
        var voidCandidates = createdOrders.Where(o => _random.NextDouble() < 0.04).ToList();
        foreach (var order in voidCandidates)
        {
            order.Status = OrderStatus.Voided;
            order.VoidReason = "Customer changed mind";
        }
        if (voidCandidates.Count > 0)
        {
            await db.SaveChangesAsync();
        }

        // Crew meals on ~90% of trips, up to each trip's company allowance.
        foreach (var trip in trips)
        {
            if (_random.NextDouble() >= 0.9)
            {
                continue;
            }

            var company = await db.BusCompanies.FirstAsync(c => c.Id == trip.BusCompanyId);
            var mealsToGive = _random.Next(1, company.CrewMealAllowancePerTrip + 1);
            var roles = Enum.GetValues<CrewRole>();

            for (var m = 0; m < mealsToGive; m++)
            {
                sequence++;
                var item = menuItems[_random.Next(menuItems.Count)];
                var crewOrder = new Order
                {
                    OrderNumber = $"{day:yyyyMMdd}-{sequence:D3}",
                    CreatedAt = trip.ArrivedAt.AddMinutes(_random.Next(5, 20)),
                    Status = OrderStatus.Completed,
                    PaymentMethod = PaymentMethod.Cash,
                    BusTripId = trip.Id,
                    IsCrewMeal = true,
                    AmountTendered = 0m,
                    ChangeGiven = 0m,
                    Lines = { new OrderLine { MenuItemId = item.Id, Quantity = 1, UnitPriceAtSale = 0m } }
                };

                db.Orders.Add(crewOrder);
                db.CrewMealCredits.Add(new CrewMealCredit
                {
                    BusTripId = trip.Id,
                    CrewRole = roles[m % roles.Length],
                    Order = crewOrder,
                    LoggedAt = crewOrder.CreatedAt
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
