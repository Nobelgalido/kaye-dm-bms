using FluentAssertions;
using KayeDM.Application.Inventory;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Inventory;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Inventory;

public class InventoryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly InventoryService _sut;

    public InventoryServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new InventoryService(new TestDbContextFactory(options));

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
    public async Task GetTodaysAvailabilityAsync_ReturnsNullAvailability_WhenNoBatchLoggedToday()
    {
        var result = await _sut.GetTodaysAvailabilityAsync();

        result.Should().ContainSingle(r => r.MenuItemId == 1 && !r.HasBatchToday && r.AvailableServings == null);
    }

    [Fact]
    public async Task GetTodaysAvailabilityAsync_ComputesRemaining_WhenBatchExists()
    {
        await _sut.CreateBatchAsync(new CreateDishBatchRequest(1, 2m, 10));

        var result = await _sut.GetTodaysAvailabilityAsync();

        result.Should().ContainSingle(r => r.MenuItemId == 1 && r.HasBatchToday && r.AvailableServings == 20);
    }

    [Fact]
    public async Task GetVarianceAsync_ReturnsOneRowPerDishPerDay_WithComputedVariancePercent()
    {
        await _sut.CreateBatchAsync(new CreateDishBatchRequest(1, 2m, 10));
        var today = DateOnly.FromDateTime(DateTime.Now.Date);

        var rows = await _sut.GetVarianceAsync(today, today);

        rows.Should().ContainSingle();
        rows[0].Produced.Should().Be(20);
        rows[0].VariancePercent.Should().Be(100m);
    }
}
