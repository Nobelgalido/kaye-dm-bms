using FluentAssertions;
using KayeDM.Application.Expenses;
using KayeDM.Application.Orders;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Expenses;
using KayeDM.Infrastructure.Orders;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Closing;

public class ClosingLockTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly OrderService _orderService;
    private readonly ExpenseService _expenseService;
    private readonly DateTime _today = DateTime.Now.Date;

    public ClosingLockTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _orderService = new OrderService(new TestDbContextFactory(options));
        _expenseService = new ExpenseService(new TestDbContextFactory(options));

        _db.MenuItems.Add(new MenuItem { Id = 1, Name = "Adobo", Category = MenuCategory.Ulam, Price = 90m, IsActive = true, SortOrder = 1 });
        _db.ExpenseCategories.Add(new ExpenseCategory { Id = 1, Name = "Ingredients", Type = ExpenseCategoryType.Ingredients, IsActive = true });
        _db.DailyClosings.Add(new DailyClosing { Date = _today, ClosedById = "u1", ClosedAt = DateTime.Now });
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
    public async Task CreateOrderAsync_Throws_WhenTodayIsClosed()
    {
        var request = new CreateOrderRequest(new[] { new OrderLineRequest(1, 1) }, PaymentMethod.Cash, 100m, null);

        var act = async () => await _orderService.CreateOrderAsync(request);

        await act.Should().ThrowAsync<DateClosedException>();
    }

    [Fact]
    public async Task VoidOrderAsync_Throws_WhenOrderDateIsClosed()
    {
        // Insert directly (bypassing CreateOrderAsync, which is itself now
        // guarded) to get a completed order dated today under a closing.
        var order = new Order
        {
            OrderNumber = "20260705-999",
            CreatedAt = _today,
            Status = OrderStatus.Completed,
            PaymentMethod = PaymentMethod.Cash,
            Lines = { new OrderLine { MenuItemId = 1, Quantity = 1, UnitPriceAtSale = 90m } }
        };
        _db.Orders.Add(order);
        _db.SaveChanges();

        var act = async () => await _orderService.VoidOrderAsync(order.Id, "customer changed mind");

        await act.Should().ThrowAsync<DateClosedException>();
    }

    [Fact]
    public async Task CreateExpenseAsync_Throws_WhenDateIsClosed()
    {
        var request = new CreateExpenseRequest(_today, 1, "Rice", 500m, ExpensePaymentMethod.Cash, null, null, "u1");

        var act = async () => await _expenseService.CreateExpenseAsync(request);

        await act.Should().ThrowAsync<DateClosedException>();
    }

    [Fact]
    public async Task UpdateExpenseAsync_Throws_WhenExpenseDateIsClosed()
    {
        var expense = new Expense
        {
            Date = _today,
            ExpenseCategoryId = 1,
            Description = "Rice",
            Amount = 500m,
            PaymentMethod = ExpensePaymentMethod.Cash,
            LoggedById = "u1",
            LoggedAt = DateTime.Now
        };
        _db.Expenses.Add(expense);
        _db.SaveChanges();

        var request = new UpdateExpenseRequest(_today, 1, "Rice (corrected)", 550m, ExpensePaymentMethod.Cash, null, null);
        var act = async () => await _expenseService.UpdateExpenseAsync(expense.Id, request);

        await act.Should().ThrowAsync<DateClosedException>();
    }
}
