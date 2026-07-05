using FluentAssertions;
using KayeDM.Application.Orders;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Orders;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Orders;

public class OversellOrderServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly OrderService _sut;
    private readonly DateTime _today = DateTime.Now.Date;

    public OversellOrderServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new OrderService(new TestDbContextFactory(options));

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
    public async Task CreateOrderAsync_Throws_WhenQuantityExceedsAvailability_AndNoOverride()
    {
        _db.DishBatches.Add(new DishBatch { Id = 1, MenuItemId = 1, Date = _today, TraysProduced = 1m, ServingsPerTray = 2, ProducedAt = DateTime.Now });
        _db.SaveChanges();

        var request = new CreateOrderRequest(new[] { new OrderLineRequest(1, 3) }, PaymentMethod.Cash, 300m, null);

        var act = async () => await _sut.CreateOrderAsync(request);

        await act.Should().ThrowAsync<OversoldException>();
    }

    [Fact]
    public async Task CreateOrderAsync_Succeeds_AndFlagsOrder_WhenOverrideConfirmed()
    {
        _db.DishBatches.Add(new DishBatch { Id = 1, MenuItemId = 1, Date = _today, TraysProduced = 1m, ServingsPerTray = 2, ProducedAt = DateTime.Now });
        _db.SaveChanges();

        var request = new CreateOrderRequest(new[] { new OrderLineRequest(1, 3) }, PaymentMethod.Cash, 300m, null, OversoldOverride: true);

        var result = await _sut.CreateOrderAsync(request);

        var order = await _db.Orders.FindAsync(result.Id);
        order!.OversoldOverride.Should().BeTrue();
    }

    [Fact]
    public async Task CreateOrderAsync_DoesNotFlagOrder_WhenOverrideTrueButNothingWasOversold()
    {
        _db.DishBatches.Add(new DishBatch { Id = 1, MenuItemId = 1, Date = _today, TraysProduced = 5m, ServingsPerTray = 10, ProducedAt = DateTime.Now });
        _db.SaveChanges();

        var request = new CreateOrderRequest(new[] { new OrderLineRequest(1, 1) }, PaymentMethod.Cash, 100m, null, OversoldOverride: true);

        var result = await _sut.CreateOrderAsync(request);

        var order = await _db.Orders.FindAsync(result.Id);
        order!.OversoldOverride.Should().BeFalse();
    }

    [Fact]
    public async Task CreateOrderAsync_Succeeds_WhenNoBatchLoggedToday_RegardlessOfQuantity()
    {
        var request = new CreateOrderRequest(new[] { new OrderLineRequest(1, 50) }, PaymentMethod.Cash, 5000m, null);

        var act = async () => await _sut.CreateOrderAsync(request);

        await act.Should().NotThrowAsync();
    }
}
