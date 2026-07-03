namespace KayeDM.Application.Buses;

public record BusCompanyDto(int Id, string Name, string? ContactPerson, int CrewMealAllowancePerTrip, bool IsActive);

public record BusCompanyUpsertDto(string Name, string? ContactPerson, int CrewMealAllowancePerTrip);

public record LogArrivalRequest(int BusCompanyId, string BusNumber, string Route, int? EstimatedPassengers);

public record BusTripDto(
    int Id,
    int BusCompanyId,
    string BusCompanyName,
    string BusNumber,
    DateTime ArrivedAt,
    DateTime? DepartedAt,
    string Route,
    int? EstimatedPassengers);

public record BusTripBoardRow(BusTripDto Trip, int MealsUsed, int AllowanceRemaining);

public record CrewMealReportRow(
    DateTime ArrivedAt,
    string BusNumber,
    string Route,
    int DriverCredits,
    int ConductorCredits,
    int AssistantCredits,
    int TotalCredits);

public record CrewMealReportResult(
    int CompanyId,
    string CompanyName,
    int Year,
    int Month,
    IReadOnlyList<CrewMealReportRow> Trips,
    int TotalTrips,
    int TotalMeals,
    int DriverMealsTotal,
    int ConductorMealsTotal,
    int AssistantMealsTotal);
