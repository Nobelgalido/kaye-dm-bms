using FluentAssertions;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Buses;
using KayeDM.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Buses;

public class BusServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly BusService _sut;

    public BusServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new BusService(new TestDbContextFactory(options));

        _db.BusCompanies.Add(new BusCompany { Id = 1, Name = "DLTB", ContactPerson = "Juan", CrewMealAllowancePerTrip = 3, IsActive = true });
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
    public async Task GetRecentArrivalsAsync_ExcludesTripsOutsideWindow()
    {
        _db.BusTrips.AddRange(
            new BusTrip { Id = 1, BusCompanyId = 1, BusNumber = "8112", Route = "Manila-Sorsogon", ArrivedAt = DateTime.Now.AddMinutes(-10) },
            new BusTrip { Id = 2, BusCompanyId = 1, BusNumber = "9001", Route = "Manila-Sorsogon", ArrivedAt = DateTime.Now.AddMinutes(-60) });
        _db.SaveChanges();

        var result = await _sut.GetRecentArrivalsAsync(TimeSpan.FromMinutes(45));

        result.Should().ContainSingle(t => t.Id == 1);
    }

    [Fact]
    public async Task GetRecentArrivalsAsync_ExcludesDepartedTrips()
    {
        _db.BusTrips.Add(new BusTrip
        {
            Id = 1,
            BusCompanyId = 1,
            BusNumber = "8112",
            Route = "Manila-Sorsogon",
            ArrivedAt = DateTime.Now.AddMinutes(-10),
            DepartedAt = DateTime.Now.AddMinutes(-2)
        });
        _db.SaveChanges();

        var result = await _sut.GetRecentArrivalsAsync(TimeSpan.FromMinutes(45));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllowanceRemainingAsync_SubtractsCreditsUsed_FromAllowance()
    {
        _db.BusTrips.Add(new BusTrip { Id = 1, BusCompanyId = 1, BusNumber = "8112", Route = "Manila-Sorsogon", ArrivedAt = DateTime.Now });
        _db.SaveChanges();

        var order = new Order
        {
            OrderNumber = "20260704-001",
            CreatedAt = DateTime.Now,
            Status = OrderStatus.Completed,
            PaymentMethod = PaymentMethod.Cash,
            IsCrewMeal = true,
            BusTripId = 1
        };
        _db.Orders.Add(order);
        _db.SaveChanges();

        _db.CrewMealCredits.Add(new CrewMealCredit { BusTripId = 1, CrewRole = CrewRole.Driver, Order = order, LoggedAt = DateTime.Now });
        _db.SaveChanges();

        var remaining = await _sut.GetAllowanceRemainingAsync(1);

        remaining.Should().Be(2);
    }
}
