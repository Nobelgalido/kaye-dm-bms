using KayeDM.Domain.Enums;

namespace KayeDM.Domain.Entities;

public class Order
{
    public int Id { get; set; }

    // Daily sequence, e.g. "20260703-041".
    public string OrderNumber { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // Identity user id (AspNetUsers.Id). Nullable this week — no login UI until Week 4.
    public string? CashierId { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Completed;
    public PaymentMethod PaymentMethod { get; set; }
    public decimal AmountTendered { get; set; }
    public decimal ChangeGiven { get; set; }

    // TODO Week 2: FK to BusTrip
    public int? BusTripId { get; set; }

    public bool IsCrewMeal { get; set; } = false;

    // Set by IOrderService.VoidOrderAsync. Blueprint §4 doesn't list a storage
    // column for this, but domain rule 4 ("voiding requires a reason") needs one.
    public string? VoidReason { get; set; }

    public List<OrderLine> Lines { get; set; } = new();
}
