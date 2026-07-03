namespace KayeDM.Domain.Entities;

public class BusCompany
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }

    // Free crew meals allowed per trip stop — the accounting obligation that
    // makes the bus company stop here.
    public int CrewMealAllowancePerTrip { get; set; }

    public bool IsActive { get; set; } = true;
}
