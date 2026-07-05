using FluentAssertions;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Inventory;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Inventory;

public class AvailabilityCalculatorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly DateTime _today = DateTime.Now.Date;

    public AvailabilityCalculatorTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _db.MenuItems.Add(new MenuItem { Id = 1, Name = "Adobo", Category = MenuCategory.Ulam, Price = 90m, IsActive = true, SortOrder = 1 });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetProducedServingsAsync_MultipliesTraysByServingsPerTray_AcrossBatchesSameDay()
    {
        _db.DishBatches.AddRange(
            new DishBatch { Id = 1, MenuItemId = 1, Date = _today, TraysProduced = 3m, ServingsPerTray = 10, ProducedAt = DateTime.Now },
            new DishBatch { Id = 2, MenuItemId = 1, Date = _today, TraysProduced = 0.5m, ServingsPerTray = 10, ProducedAt = DateTime.Now });
        _db.SaveChanges();

        var produced = await AvailabilityCalculator.GetProducedServingsAsync(_db, 1, _today);

        produced.Should().Be(35);
    }

    [Fact]
    public async Task GetSoldServingsAsync_ExcludesVoidedOrders()
    {
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260704-001",
            CreatedAt = DateTime.Now,
            Status = OrderStatus.Voided,
            PaymentMethod = PaymentMethod.Cash,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 5, UnitPriceAtSale = 90m } }
        });
        _db.SaveChanges();

        var sold = await AvailabilityCalculator.GetSoldServingsAsync(_db, 1, _today);

        sold.Should().Be(0);
    }

    [Fact]
    public async Task GetSoldServingsAsync_IncludesCrewMealOrders_BecauseTheDriverStillAteTheFood()
    {
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260704-002",
            CreatedAt = DateTime.Now,
            Status = OrderStatus.Completed,
            PaymentMethod = PaymentMethod.Cash,
            IsCrewMeal = true,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 3, UnitPriceAtSale = 0m } }
        });
        _db.SaveChanges();

        var sold = await AvailabilityCalculator.GetSoldServingsAsync(_db, 1, _today);

        sold.Should().Be(3);
    }

    [Fact]
    public async Task GetWastedServingsAsync_MultipliesTraysWastedByBatchServingsPerTray()
    {
        _db.DishBatches.Add(new DishBatch { Id = 1, MenuItemId = 1, Date = _today, TraysProduced = 5m, ServingsPerTray = 10, ProducedAt = DateTime.Now });
        _db.SaveChanges();
        _db.WasteLogs.Add(new WasteLog { DishBatchId = 1, TraysWasted = 1.5m, Reason = WasteReason.EndOfDay, LoggedAt = DateTime.Now, LoggedById = "system" });
        _db.SaveChanges();

        var wasted = await AvailabilityCalculator.GetWastedServingsAsync(_db, 1, _today);

        wasted.Should().Be(15);
    }

    [Fact]
    public async Task GetAvailableServingsAsync_SubtractsSoldAndWasted_FromProduced()
    {
        _db.DishBatches.Add(new DishBatch { Id = 1, MenuItemId = 1, Date = _today, TraysProduced = 2m, ServingsPerTray = 10, ProducedAt = DateTime.Now });
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260704-003",
            CreatedAt = DateTime.Now,
            Status = OrderStatus.Completed,
            PaymentMethod = PaymentMethod.Cash,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 4, UnitPriceAtSale = 90m } }
        });
        _db.SaveChanges();
        _db.WasteLogs.Add(new WasteLog { DishBatchId = 1, TraysWasted = 1m, Reason = WasteReason.Spoiled, LoggedAt = DateTime.Now, LoggedById = "system" });
        _db.SaveChanges();

        var available = await AvailabilityCalculator.GetAvailableServingsAsync(_db, 1, _today);

        // 20 produced - 4 sold - 10 wasted = 6
        available.Should().Be(6);
    }
}
