using KayeDM.Domain.Enums;

namespace KayeDM.Domain.Entities;

public class CrewMealCredit
{
    public int Id { get; set; }
    public int BusTripId { get; set; }
    public BusTrip BusTrip { get; set; } = null!;
    public CrewRole CrewRole { get; set; }
    public int OrderId { get; set; }

    // Tracked as a navigation (not just OrderId) so OrderService can add the
    // Order and this credit to the same DbContext and save once — EF fixes up
    // OrderId from the Order's generated Id in that single SaveChangesAsync,
    // making order + credit creation atomic.
    public Order Order { get; set; } = null!;

    public DateTime LoggedAt { get; set; }
}
