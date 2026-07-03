namespace KayeDM.Domain.Entities;

public class BusTrip
{
    public int Id { get; set; }
    public int BusCompanyId { get; set; }
    public BusCompany BusCompany { get; set; } = null!;
    public string BusNumber { get; set; } = string.Empty;
    public DateTime ArrivedAt { get; set; }
    public DateTime? DepartedAt { get; set; }
    public string Route { get; set; } = string.Empty;
    public int? EstimatedPassengers { get; set; }
}
