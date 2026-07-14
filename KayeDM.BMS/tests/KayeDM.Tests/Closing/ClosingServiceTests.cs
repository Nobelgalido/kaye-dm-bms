using FluentAssertions;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Closing;
using KayeDM.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Closing;

public class ClosingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly ClosingService _sut;
    private readonly DateTime _today = DateTime.Now.Date;

    public ClosingServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new ClosingService(new TestDbContextFactory(options));

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
    public async Task GetTodaysFiguresAsync_ComputesNetForDay_FromSalesMinusExpenses()
    {
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-001",
            CreatedAt = DateTime.Now,
            Status = OrderStatus.Completed,
            PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 100m,
            ChangeGiven = 10m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 90m } }
        });
        _db.ExpenseCategories.Add(new ExpenseCategory { Id = 1, Name = "Ingredients", Type = ExpenseCategoryType.Ingredients, IsActive = true });
        _db.SaveChanges();
        _db.Expenses.Add(new Expense { Date = _today, ExpenseCategoryId = 1, Description = "Rice", Amount = 30m, PaymentMethod = ExpensePaymentMethod.Cash, LoggedById = "u1", LoggedAt = DateTime.Now });
        _db.SaveChanges();

        var figures = await _sut.GetTodaysFiguresAsync();

        figures.TotalSales.Should().Be(90m);
        figures.TotalExpenses.Should().Be(30m);
        figures.NetForDay.Should().Be(60m);
    }

    [Fact]
    public async Task CreateClosingAsync_PersistsSnapshot_MatchingTodaysFigures()
    {
        _db.Orders.Add(new Order
        {
            OrderNumber = "20260705-001",
            CreatedAt = DateTime.Now,
            Status = OrderStatus.Completed,
            PaymentMethod = PaymentMethod.Cash,
            AmountTendered = 90m,
            ChangeGiven = 0m,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 90m } }
        });
        _db.SaveChanges();

        var closing = await _sut.CreateClosingAsync("owner-1");

        closing.TotalSales.Should().Be(90m);
        closing.ClosedById.Should().Be("owner-1");
    }

    [Fact]
    public async Task CreateClosingAsync_Throws_WhenTodayAlreadyClosed()
    {
        await _sut.CreateClosingAsync("owner-1");

        var act = async () => await _sut.CreateClosingAsync("owner-1");

        await act.Should().ThrowAsync<KayeDM.Domain.Exceptions.DomainException>();
    }

    [Fact]
    public async Task CreateClosingAsync_ThrowsDomainException_NotDbUpdateException_WhenClosingWasCreatedConcurrently()
    {
        // Simulates a second request that committed a closing for today
        // strictly between this caller's read and its own save — the unique
        // index on DailyClosing.Date is the sole guard, so this must surface
        // as the same friendly DomainException as the sequential case, not
        // a raw DbUpdateException bubbling out of SaveChangesAsync.
        _db.DailyClosings.Add(new DailyClosing { Date = _today, TotalSales = 50m, ClosedById = "owner-2", ClosedAt = DateTime.Now });
        _db.SaveChanges();

        var act = async () => await _sut.CreateClosingAsync("owner-1");

        var exception = await act.Should().ThrowAsync<KayeDM.Domain.Exceptions.DomainException>();
        exception.Which.Should().NotBeOfType<Microsoft.EntityFrameworkCore.DbUpdateException>();
    }

    [Fact]
    public async Task IsDateClosedAsync_ReturnsTrue_ForDatesOnOrBeforeAClosing()
    {
        await _sut.CreateClosingAsync("owner-1");

        var todayClosed = await _sut.IsDateClosedAsync(DateOnly.FromDateTime(_today));
        var yesterdayClosed = await _sut.IsDateClosedAsync(DateOnly.FromDateTime(_today.AddDays(-1)));
        var tomorrowClosed = await _sut.IsDateClosedAsync(DateOnly.FromDateTime(_today.AddDays(1)));

        todayClosed.Should().BeTrue();
        yesterdayClosed.Should().BeTrue();
        tomorrowClosed.Should().BeFalse();
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsEmptyList_WhenNoDaysClosed()
    {
        var history = await _sut.GetHistoryAsync();

        history.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsClosings_OrderedByDateDescending()
    {
        _db.DailyClosings.Add(new DailyClosing
        {
            Date = _today.AddDays(-2),
            TotalSales = 500m,
            TotalExpenses = 100m,
            NetForDay = 400m,
            ClosedById = "owner-1",
            ClosedAt = _today.AddDays(-2).AddHours(20)
        });
        _db.DailyClosings.Add(new DailyClosing
        {
            Date = _today.AddDays(-1),
            TotalSales = 600m,
            TotalExpenses = 150m,
            NetForDay = 450m,
            ClosedById = "owner-1",
            ClosedAt = _today.AddDays(-1).AddHours(20)
        });
        _db.SaveChanges();

        var history = await _sut.GetHistoryAsync();

        history.Should().HaveCount(2);
        history[0].Date.Should().Be(DateOnly.FromDateTime(_today.AddDays(-1)));
        history[1].Date.Should().Be(DateOnly.FromDateTime(_today.AddDays(-2)));
        history[0].NetForDay.Should().Be(450m);
    }
}
