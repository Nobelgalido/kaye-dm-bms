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

public class CrewMealOrderServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly OrderService _sut;

    public CrewMealOrderServiceTests()
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
        _db.BusCompanies.Add(new BusCompany { Id = 1, Name = "DLTB", CrewMealAllowancePerTrip = 2, IsActive = true });
        _db.BusTrips.Add(new BusTrip { Id = 1, BusCompanyId = 1, BusNumber = "8112", Route = "Manila-Sorsogon", ArrivedAt = DateTime.Now });
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
    public async Task CreateCrewMealOrderAsync_ForcesTotalAndTenderedToZero()
    {
        var request = new CreateCrewMealOrderRequest(new[] { new OrderLineRequest(1, 2) }, BusTripId: 1, CrewRole: CrewRole.Driver, CashierId: null);

        var result = await _sut.CreateCrewMealOrderAsync(request);

        result.Total.Should().Be(0m);
        result.AmountTendered.Should().Be(0m);
        result.ChangeGiven.Should().Be(0m);
    }

    [Fact]
    public async Task CreateCrewMealOrderAsync_CreatesExactlyOneCrewMealCreditLinkedToOrder()
    {
        var request = new CreateCrewMealOrderRequest(new[] { new OrderLineRequest(1, 1) }, BusTripId: 1, CrewRole: CrewRole.Conductor, CashierId: null);

        var result = await _sut.CreateCrewMealOrderAsync(request);

        var credits = await _db.CrewMealCredits.Where(c => c.OrderId == result.Id).ToListAsync();
        credits.Should().ContainSingle();
        credits[0].CrewRole.Should().Be(CrewRole.Conductor);
        credits[0].BusTripId.Should().Be(1);

        var order = await _db.Orders.FindAsync(result.Id);
        order!.IsCrewMeal.Should().BeTrue();
    }

    [Fact]
    public async Task CreateCrewMealOrderAsync_Succeeds_AtExactAllowanceLimit()
    {
        var request = new CreateCrewMealOrderRequest(new[] { new OrderLineRequest(1, 1) }, BusTripId: 1, CrewRole: CrewRole.Driver, CashierId: null);

        // Allowance is 2 — both of the first two crew meal orders for this trip must succeed.
        await _sut.CreateCrewMealOrderAsync(request);
        var act = async () => await _sut.CreateCrewMealOrderAsync(request);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateCrewMealOrderAsync_Throws_WhenAllowanceExceeded()
    {
        var request = new CreateCrewMealOrderRequest(new[] { new OrderLineRequest(1, 1) }, BusTripId: 1, CrewRole: CrewRole.Driver, CashierId: null);

        // Allowance is 2 — the third crew meal order for this trip must be rejected.
        await _sut.CreateCrewMealOrderAsync(request);
        await _sut.CreateCrewMealOrderAsync(request);
        var act = async () => await _sut.CreateCrewMealOrderAsync(request);

        await act.Should().ThrowAsync<DomainException>();
    }
}
