using FluentAssertions;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Closing;
using KayeDM.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Tests.Closing;

public class ClosingGuardTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;

    public ClosingGuardTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task EnsureDateNotClosedAsync_Throws_WhenExactDateIsClosed()
    {
        var today = DateTime.Now.Date;
        _db.DailyClosings.Add(new DailyClosing { Date = today, ClosedById = "u1", ClosedAt = DateTime.Now });
        _db.SaveChanges();

        var act = async () => await ClosingGuard.EnsureDateNotClosedAsync(_db, today, "create order");

        await act.Should().ThrowAsync<DateClosedException>();
    }

    [Fact]
    public async Task EnsureDateNotClosedAsync_Throws_WhenAnEarlierDateThanAClosedDate()
    {
        var today = DateTime.Now.Date;
        _db.DailyClosings.Add(new DailyClosing { Date = today, ClosedById = "u1", ClosedAt = DateTime.Now });
        _db.SaveChanges();

        var yesterday = today.AddDays(-1);
        var act = async () => await ClosingGuard.EnsureDateNotClosedAsync(_db, yesterday, "create expense");

        await act.Should().ThrowAsync<DateClosedException>();
    }

    [Fact]
    public async Task EnsureDateNotClosedAsync_DoesNotThrow_WhenNoClosingCoversTheDate()
    {
        var today = DateTime.Now.Date;

        var act = async () => await ClosingGuard.EnsureDateNotClosedAsync(_db, today, "create order");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureDateNotClosedAsync_DoesNotThrow_WhenOnlyAnEarlierClosingExists()
    {
        var today = DateTime.Now.Date;
        _db.DailyClosings.Add(new DailyClosing { Date = today.AddDays(-2), ClosedById = "u1", ClosedAt = DateTime.Now });
        _db.SaveChanges();

        var act = async () => await ClosingGuard.EnsureDateNotClosedAsync(_db, today, "create order");

        await act.Should().NotThrowAsync();
    }
}
