using KayeDM.Domain.Enums;

namespace KayeDM.Domain.Entities;

public class WasteLog
{
    public int Id { get; set; }
    public int DishBatchId { get; set; }
    public DishBatch DishBatch { get; set; } = null!;
    public decimal TraysWasted { get; set; }
    public WasteReason Reason { get; set; }
    public DateTime LoggedAt { get; set; }

    // Plain string until auth in Week 4.
    // TODO Week 4: replace with real Identity user id.
    public string LoggedById { get; set; } = string.Empty;
}
