using KayeDM.Application.Buses;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Buses;

public class BusService : IBusService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public BusService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<List<BusCompanyDto>> GetCompaniesAsync(bool includeInactive = false)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var query = db.BusCompanies.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query
            .OrderBy(c => c.Name)
            .Select(c => new BusCompanyDto(c.Id, c.Name, c.ContactPerson, c.CrewMealAllowancePerTrip, c.IsActive))
            .ToListAsync();
    }

    public async Task<BusCompanyDto> CreateCompanyAsync(BusCompanyUpsertDto dto)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = new BusCompany
        {
            Name = dto.Name,
            ContactPerson = dto.ContactPerson,
            CrewMealAllowancePerTrip = dto.CrewMealAllowancePerTrip,
            IsActive = true
        };

        db.BusCompanies.Add(entity);
        await db.SaveChangesAsync();

        return new BusCompanyDto(entity.Id, entity.Name, entity.ContactPerson, entity.CrewMealAllowancePerTrip, entity.IsActive);
    }

    public async Task<BusCompanyDto> UpdateCompanyAsync(int id, BusCompanyUpsertDto dto)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.BusCompanies.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new DomainException($"Bus company {id} not found.");

        entity.Name = dto.Name;
        entity.ContactPerson = dto.ContactPerson;
        entity.CrewMealAllowancePerTrip = dto.CrewMealAllowancePerTrip;

        await db.SaveChangesAsync();

        return new BusCompanyDto(entity.Id, entity.Name, entity.ContactPerson, entity.CrewMealAllowancePerTrip, entity.IsActive);
    }

    public async Task SetCompanyActiveAsync(int id, bool isActive)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.BusCompanies.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new DomainException($"Bus company {id} not found.");

        entity.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    public async Task<BusTripDto> LogArrivalAsync(LogArrivalRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var company = await db.BusCompanies.FirstOrDefaultAsync(c => c.Id == request.BusCompanyId)
            ?? throw new DomainException($"Bus company {request.BusCompanyId} not found.");

        if (string.IsNullOrWhiteSpace(request.BusNumber))
        {
            throw new DomainException("Bus number is required.");
        }

        var trip = new BusTrip
        {
            BusCompanyId = request.BusCompanyId,
            BusNumber = request.BusNumber,
            Route = request.Route,
            EstimatedPassengers = request.EstimatedPassengers,
            ArrivedAt = DateTime.Now
        };

        db.BusTrips.Add(trip);
        await db.SaveChangesAsync();

        return new BusTripDto(trip.Id, company.Id, company.Name, trip.BusNumber, trip.ArrivedAt, trip.DepartedAt, trip.Route, trip.EstimatedPassengers);
    }

    public async Task DepartAsync(int tripId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var trip = await db.BusTrips.FirstOrDefaultAsync(t => t.Id == tripId)
            ?? throw new DomainException($"Bus trip {tripId} not found.");

        trip.DepartedAt = DateTime.Now;
        await db.SaveChangesAsync();
    }

    public async Task<List<BusTripBoardRow>> GetTodaysTripBoardAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var today = DateTime.Now.Date;
        var trips = await db.BusTrips
            .AsNoTracking()
            .Include(t => t.BusCompany)
            .Where(t => t.ArrivedAt >= today && t.ArrivedAt < today.AddDays(1))
            .OrderByDescending(t => t.ArrivedAt)
            .ToListAsync();

        var board = new List<BusTripBoardRow>();
        foreach (var trip in trips)
        {
            var mealsUsed = await db.CrewMealCredits.CountAsync(c => c.BusTripId == trip.Id);
            var dto = new BusTripDto(trip.Id, trip.BusCompanyId, trip.BusCompany.Name, trip.BusNumber, trip.ArrivedAt, trip.DepartedAt, trip.Route, trip.EstimatedPassengers);
            board.Add(new BusTripBoardRow(dto, mealsUsed, trip.BusCompany.CrewMealAllowancePerTrip - mealsUsed));
        }

        return board;
    }

    public async Task<List<BusTripDto>> GetRecentArrivalsAsync(TimeSpan window)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var cutoff = DateTime.Now - window;

        return await db.BusTrips
            .AsNoTracking()
            .Include(t => t.BusCompany)
            .Where(t => t.ArrivedAt >= cutoff && t.DepartedAt == null)
            .OrderByDescending(t => t.ArrivedAt)
            .Select(t => new BusTripDto(t.Id, t.BusCompanyId, t.BusCompany.Name, t.BusNumber, t.ArrivedAt, t.DepartedAt, t.Route, t.EstimatedPassengers))
            .ToListAsync();
    }

    public async Task<int> GetAllowanceRemainingAsync(int tripId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var trip = await db.BusTrips.Include(t => t.BusCompany).FirstOrDefaultAsync(t => t.Id == tripId)
            ?? throw new DomainException($"Bus trip {tripId} not found.");

        var used = await db.CrewMealCredits.CountAsync(c => c.BusTripId == tripId);
        return trip.BusCompany.CrewMealAllowancePerTrip - used;
    }

    public async Task<CrewMealReportResult> GetMonthlyReportAsync(int companyId, int year, int month)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var company = await db.BusCompanies.FirstOrDefaultAsync(c => c.Id == companyId)
            ?? throw new DomainException($"Bus company {companyId} not found.");

        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var trips = await db.BusTrips
            .AsNoTracking()
            .Where(t => t.BusCompanyId == companyId && t.ArrivedAt >= monthStart && t.ArrivedAt < monthEnd)
            .OrderBy(t => t.ArrivedAt)
            .ToListAsync();

        var rows = new List<CrewMealReportRow>();
        foreach (var trip in trips)
        {
            var credits = await db.CrewMealCredits.Where(c => c.BusTripId == trip.Id).ToListAsync();
            var driverCount = credits.Count(c => c.CrewRole == CrewRole.Driver);
            var conductorCount = credits.Count(c => c.CrewRole == CrewRole.Conductor);
            var assistantCount = credits.Count(c => c.CrewRole == CrewRole.Assistant);
            rows.Add(new CrewMealReportRow(trip.ArrivedAt, trip.BusNumber, trip.Route, driverCount, conductorCount, assistantCount, driverCount + conductorCount + assistantCount));
        }

        return new CrewMealReportResult(
            company.Id,
            company.Name,
            year,
            month,
            rows,
            TotalTrips: rows.Count,
            TotalMeals: rows.Sum(r => r.TotalCredits),
            DriverMealsTotal: rows.Sum(r => r.DriverCredits),
            ConductorMealsTotal: rows.Sum(r => r.ConductorCredits),
            AssistantMealsTotal: rows.Sum(r => r.AssistantCredits));
    }
}
