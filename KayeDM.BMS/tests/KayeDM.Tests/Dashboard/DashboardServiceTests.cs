using FluentAssertions;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Dashboard;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Dashboard;

public class DashboardServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly DashboardService _sut;
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Now);

    public DashboardServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new DashboardService(new TestDbContextFactory(options));

        _db.MenuItems.Add(new MenuItem { Id = 1, Name = "Adobo", Category = MenuCategory.Ulam, Price = 90m, IsActive = true, SortOrder = 1 });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AppDbContext> options)
        {
            _options = options;
        }

        public AppDbContext CreateDbContext() => new(_options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AppDbContext(_options));
    }

    [Fact]
    public async Task GetKpisAsync_ComputesNetProfit_AndCrewMealsEstimatedCost()
    {
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-001", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 90m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 90m } }
        });
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-002", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            IsCrewMeal = true, AmountTendered = 0m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 0m } }
        });
        _db.ExpenseCategories.Add(new ExpenseCategory { Id = 1, Name = "Ingredients", Type = ExpenseCategoryType.Ingredients, IsActive = true });
        _db.SaveChanges();
        _db.Expenses.Add(new Expense { Date = DateTime.Now.Date, ExpenseCategoryId = 1, Description = "Rice", Amount = 20m, PaymentMethod = ExpensePaymentMethod.Cash, LoggedById = "u1", LoggedAt = DateTime.Now });
        _db.SaveChanges();

        var kpis = await _sut.GetKpisAsync(_today, _today);

        kpis.TotalSales.Should().Be(90m);
        kpis.NetProfit.Should().Be(70m);
        kpis.CrewMealsGiven.Should().Be(1);
        kpis.CrewMealsEstimatedCost.Should().Be(90m);
    }

    [Fact]
    public async Task GetSalesByHourAsync_BucketsOrdersByHour_AndIncludesArrivalMarkers()
    {
        var hourNow = DateTime.Now.Hour;
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-001", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 90m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 90m } }
        });
        _db.BusCompanies.Add(new BusCompany { Id = 1, Name = "DLTB", CrewMealAllowancePerTrip = 2, IsActive = true });
        _db.BusTrips.Add(new BusTrip { Id = 1, BusCompanyId = 1, BusNumber = "8112", Route = "Manila-Sorsogon", ArrivedAt = DateTime.Now });
        _db.SaveChanges();

        var result = await _sut.GetSalesByHourAsync(_today);

        result.Hours.Should().HaveCount(24);
        result.Hours.Single(h => h.Hour == hourNow).Sales.Should().Be(90m);
        result.Arrivals.Should().ContainSingle(a => a.CompanyName == "DLTB" && a.BusNumber == "8112");
    }

    [Fact]
    public async Task GetRevenueExpenseTrendAsync_ReturnsOnePointPerDay_WithCorrectNet()
    {
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-001", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 100m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 100m } }
        });
        _db.SaveChanges();

        var trend = await _sut.GetRevenueExpenseTrendAsync(_today, _today);

        trend.Should().ContainSingle();
        trend[0].Revenue.Should().Be(100m);
        trend[0].Net.Should().Be(100m);
    }

    [Fact]
    public async Task GetExpenseBreakdownAsync_GroupsByCategory_ForTheGivenMonth()
    {
        _db.ExpenseCategories.AddRange(
            new ExpenseCategory { Id = 1, Name = "Ingredients", Type = ExpenseCategoryType.Ingredients, IsActive = true },
            new ExpenseCategory { Id = 2, Name = "Utilities", Type = ExpenseCategoryType.Utilities, IsActive = true });
        _db.SaveChanges();
        _db.Expenses.AddRange(
            new Expense { Date = new DateTime(2026, 7, 5), ExpenseCategoryId = 1, Description = "Rice", Amount = 500m, PaymentMethod = ExpensePaymentMethod.Cash, LoggedById = "u1", LoggedAt = DateTime.Now },
            new Expense { Date = new DateTime(2026, 7, 6), ExpenseCategoryId = 2, Description = "Electric", Amount = 300m, PaymentMethod = ExpensePaymentMethod.Cash, LoggedById = "u1", LoggedAt = DateTime.Now });
        _db.SaveChanges();

        var breakdown = await _sut.GetExpenseBreakdownAsync(2026, 7);

        breakdown.Should().HaveCount(2);
        breakdown.Single(r => r.CategoryName == "Ingredients").Amount.Should().Be(500m);
    }

    [Fact]
    public async Task GetTopDishesAsync_RanksByRevenue_OverTheWindow()
    {
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-001", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 180m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 2, UnitPriceAtSale = 90m } }
        });
        _db.SaveChanges();

        var rows = await _sut.GetTopDishesAsync(7);

        rows.Should().ContainSingle();
        rows[0].Revenue.Should().Be(180m);
        rows[0].QuantitySold.Should().Be(2);
    }

    [Fact]
    public async Task GetWasteByDishAsync_ComputesWastePercent_OverTheWindow()
    {
        _db.DishBatches.Add(new DishBatch { Id = 1, MenuItemId = 1, Date = DateTime.Now.Date, TraysProduced = 2m, ServingsPerTray = 10, ProducedAt = DateTime.Now });
        _db.SaveChanges();
        _db.WasteLogs.Add(new WasteLog { DishBatchId = 1, TraysWasted = 0.5m, Reason = WasteReason.EndOfDay, LoggedAt = DateTime.Now, LoggedById = "u1" });
        _db.SaveChanges();

        var rows = await _sut.GetWasteByDishAsync(7);

        rows.Should().ContainSingle();
        rows[0].Produced.Should().Be(20);
        rows[0].Wasted.Should().Be(5);
        rows[0].WastePercent.Should().Be(25m);
    }

    [Fact]
    public async Task GetSalesPerBusCompanyAsync_SeparatesDirectFromWaveAttributedSales()
    {
        _db.BusCompanies.Add(new BusCompany { Id = 1, Name = "DLTB", CrewMealAllowancePerTrip = 2, IsActive = true });
        _db.BusTrips.Add(new BusTrip { Id = 1, BusCompanyId = 1, BusNumber = "8112", Route = "Manila-Sorsogon", ArrivedAt = DateTime.Now });
        _db.SaveChanges();

        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-001", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            BusTripId = 1, AmountTendered = 100m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 100m } }
        });
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-002", CreatedAt = DateTime.Now.AddMinutes(10), Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 50m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 50m } }
        });
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-003", CreatedAt = DateTime.Now.AddMinutes(30), Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 999m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 999m } }
        });
        _db.SaveChanges();

        var today = _today;
        var rows = await _sut.GetSalesPerBusCompanyAsync(today, today);

        var dltb = rows.Single(r => r.CompanyName == "DLTB");
        dltb.DirectSales.Should().Be(100m);
        dltb.DirectOrderCount.Should().Be(1);
        dltb.WaveAttributedSales.Should().Be(50m);
        dltb.WaveAttributedOrderCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPaymentMethodSplitAsync_GroupsByMethod()
    {
        _db.Orders.AddRange(
            new Order { OrderNumber = "20260705-001", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash, AmountTendered = 100m, ChangeGiven = 0m, Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 100m } } },
            new Order { OrderNumber = "20260705-002", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.GCash, AmountTendered = 50m, ChangeGiven = 0m, Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 50m } } });
        _db.SaveChanges();

        var split = await _sut.GetPaymentMethodSplitAsync(_today, _today);

        split.Should().HaveCount(2);
        split.Single(s => s.PaymentMethod == "Cash").Amount.Should().Be(100m);
        split.Single(s => s.PaymentMethod == "GCash").Amount.Should().Be(50m);
    }

    [Fact]
    public async Task GetInsightsAsync_ReturnsAtLeastOneInsight_WhenTrendDataExists()
    {
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-001", CreatedAt = DateTime.Now, Status = OrderStatus.Completed, PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 100m, ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 100m } }
        });
        _db.SaveChanges();

        var insights = await _sut.GetInsightsAsync(_today, _today);

        insights.Should().NotBeEmpty();
    }
}
