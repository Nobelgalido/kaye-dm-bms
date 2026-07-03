namespace KayeDM.Application.Buses;

public interface IBusService
{
    Task<List<BusCompanyDto>> GetCompaniesAsync(bool includeInactive = false);
    Task<BusCompanyDto> CreateCompanyAsync(BusCompanyUpsertDto dto);
    Task<BusCompanyDto> UpdateCompanyAsync(int id, BusCompanyUpsertDto dto);
    Task SetCompanyActiveAsync(int id, bool isActive);

    Task<BusTripDto> LogArrivalAsync(LogArrivalRequest request);
    Task DepartAsync(int tripId);
    Task<List<BusTripBoardRow>> GetTodaysTripBoardAsync();

    // Trips arrived within the given window and not yet departed — feeds the
    // POS "assign to bus" dropdown (blueprint Module A: last 45 minutes).
    Task<List<BusTripDto>> GetRecentArrivalsAsync(TimeSpan window);

    Task<int> GetAllowanceRemainingAsync(int tripId);
    Task<CrewMealReportResult> GetMonthlyReportAsync(int companyId, int year, int month);
}
