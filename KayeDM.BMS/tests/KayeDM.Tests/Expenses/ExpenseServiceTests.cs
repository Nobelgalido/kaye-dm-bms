using FluentAssertions;
using KayeDM.Application.Expenses;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Expenses;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Expenses;

public class ExpenseServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly ExpenseService _sut;

    public ExpenseServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new ExpenseService(new TestDbContextFactory(options));

        _db.ExpenseCategories.AddRange(
            new ExpenseCategory { Id = 1, Name = "Ingredients", Type = ExpenseCategoryType.Ingredients, IsActive = true },
            new ExpenseCategory { Id = 2, Name = "Utilities", Type = ExpenseCategoryType.Utilities, IsActive = true });
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
    public async Task GetMonthlySummaryAsync_GroupsByCategory_AndSumsPerMonth()
    {
        _db.Expenses.AddRange(
            new Expense { Date = new DateTime(2026, 6, 5), ExpenseCategoryId = 1, Description = "Rice", Amount = 1000m, PaymentMethod = ExpensePaymentMethod.Cash, LoggedById = "system", LoggedAt = DateTime.Now },
            new Expense { Date = new DateTime(2026, 6, 20), ExpenseCategoryId = 1, Description = "Meat", Amount = 500m, PaymentMethod = ExpensePaymentMethod.Cash, LoggedById = "system", LoggedAt = DateTime.Now },
            new Expense { Date = new DateTime(2026, 7, 3), ExpenseCategoryId = 2, Description = "Electric bill", Amount = 2000m, PaymentMethod = ExpensePaymentMethod.BankTransfer, LoggedById = "system", LoggedAt = DateTime.Now });
        _db.SaveChanges();

        var result = await _sut.GetMonthlySummaryAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 31), categoryId: null);

        result.Months.Should().Equal("2026-06", "2026-07");

        var ingredientsRow = result.Rows.Single(r => r.CategoryName == "Ingredients");
        ingredientsRow.AmountsByMonth["2026-06"].Should().Be(1500m);
        ingredientsRow.AmountsByMonth["2026-07"].Should().Be(0m);
        ingredientsRow.Total.Should().Be(1500m);

        var utilitiesRow = result.Rows.Single(r => r.CategoryName == "Utilities");
        utilitiesRow.AmountsByMonth["2026-07"].Should().Be(2000m);

        result.TotalsByMonth["2026-06"].Should().Be(1500m);
        result.TotalsByMonth["2026-07"].Should().Be(2000m);
        result.GrandTotal.Should().Be(3500m);
    }

    [Fact]
    public async Task GetMonthlySummaryAsync_FiltersByCategory_WhenProvided()
    {
        _db.Expenses.AddRange(
            new Expense { Date = new DateTime(2026, 6, 5), ExpenseCategoryId = 1, Description = "Rice", Amount = 1000m, PaymentMethod = ExpensePaymentMethod.Cash, LoggedById = "system", LoggedAt = DateTime.Now },
            new Expense { Date = new DateTime(2026, 6, 6), ExpenseCategoryId = 2, Description = "Water bill", Amount = 300m, PaymentMethod = ExpensePaymentMethod.Cash, LoggedById = "system", LoggedAt = DateTime.Now });
        _db.SaveChanges();

        var result = await _sut.GetMonthlySummaryAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), categoryId: 1);

        result.Rows.Should().ContainSingle();
        result.GrandTotal.Should().Be(1000m);
    }
}
