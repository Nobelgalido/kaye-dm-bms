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

public class OrderServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly OrderService _sut;

    public OrderServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new OrderService(new TestDbContextFactory(options));

        _db.MenuItems.AddRange(
            new MenuItem { Id = 1, Name = "Adobo", Category = MenuCategory.Ulam, Price = 90m, IsActive = true, SortOrder = 1 },
            new MenuItem { Id = 2, Name = "Rice", Category = MenuCategory.Rice, Price = 15m, IsActive = true, SortOrder = 2 });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    /// <summary>
    /// Test-only <see cref="IDbContextFactory{AppDbContext}"/> that wraps the same
    /// <see cref="DbContextOptions{AppDbContext}"/> (and therefore the same open SQLite
    /// in-memory connection) used to seed data in the test constructor, so every
    /// context created by <see cref="OrderService"/> during a test sees the same schema
    /// and data.
    /// </summary>
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
    public async Task CreateOrderAsync_ComputesTotal_FromLines()
    {
        var request = new CreateOrderRequest(
            new[] { new OrderLineRequest(1, 2), new OrderLineRequest(2, 1) },
            PaymentMethod.Cash, 200m, null);

        var result = await _sut.CreateOrderAsync(request);

        result.Total.Should().Be(195m);
    }

    [Fact]
    public async Task CreateOrderAsync_ComputesChange_FromTendered()
    {
        var request = new CreateOrderRequest(new[] { new OrderLineRequest(1, 1) }, PaymentMethod.Cash, 100m, null);

        var result = await _sut.CreateOrderAsync(request);

        result.ChangeGiven.Should().Be(10m);
    }

    [Fact]
    public async Task CreateOrderAsync_Throws_WhenTenderedLessThanTotal()
    {
        var request = new CreateOrderRequest(new[] { new OrderLineRequest(1, 1) }, PaymentMethod.Cash, 50m, null);

        var act = async () => await _sut.CreateOrderAsync(request);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task CreateOrderAsync_Throws_WhenNoLines()
    {
        var request = new CreateOrderRequest(Array.Empty<OrderLineRequest>(), PaymentMethod.Cash, 0m, null);

        var act = async () => await _sut.CreateOrderAsync(request);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task CreateOrderAsync_GeneratesSequentialDailyOrderNumbers()
    {
        var request = new CreateOrderRequest(new[] { new OrderLineRequest(2, 1) }, PaymentMethod.Cash, 20m, null);

        var first = await _sut.CreateOrderAsync(request);
        var second = await _sut.CreateOrderAsync(request);
        var third = await _sut.CreateOrderAsync(request);

        var today = DateTime.Now.ToString("yyyyMMdd");
        first.OrderNumber.Should().Be($"{today}-001");
        second.OrderNumber.Should().Be($"{today}-002");
        third.OrderNumber.Should().Be($"{today}-003");
    }

    [Fact]
    public async Task VoidOrderAsync_SetsStatusVoided_AndStoresReason()
    {
        var created = await _sut.CreateOrderAsync(
            new CreateOrderRequest(new[] { new OrderLineRequest(2, 1) }, PaymentMethod.Cash, 20m, null));

        await _sut.VoidOrderAsync(created.Id, "Customer changed mind");

        var order = await _db.Orders.FindAsync(created.Id);
        order!.Status.Should().Be(OrderStatus.Voided);
        order.VoidReason.Should().Be("Customer changed mind");
    }

    [Fact]
    public async Task VoidOrderAsync_Throws_WhenReasonIsEmpty()
    {
        var created = await _sut.CreateOrderAsync(
            new CreateOrderRequest(new[] { new OrderLineRequest(2, 1) }, PaymentMethod.Cash, 20m, null));

        var act = async () => await _sut.VoidOrderAsync(created.Id, "");

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task CreateOrderAsync_SetsBusTripId_WhenProvided()
    {
        _db.BusCompanies.Add(new BusCompany { Id = 1, Name = "DLTB", CrewMealAllowancePerTrip = 2, IsActive = true });
        _db.BusTrips.Add(new BusTrip { Id = 1, BusCompanyId = 1, BusNumber = "8112", Route = "Manila-Sorsogon", ArrivedAt = DateTime.Now });
        _db.SaveChanges();

        var request = new CreateOrderRequest(new[] { new OrderLineRequest(1, 1) }, PaymentMethod.Cash, 100m, null, BusTripId: 1);

        var result = await _sut.CreateOrderAsync(request);

        var order = await _db.Orders.FindAsync(result.Id);
        order!.BusTripId.Should().Be(1);
    }
}
