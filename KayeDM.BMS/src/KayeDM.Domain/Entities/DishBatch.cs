namespace KayeDM.Domain.Entities;

public class DishBatch
{
    public int Id { get; set; }
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;

    // Date-only in practice — always stored/queried at midnight so a batch
    // belongs to exactly one calendar day.
    public DateTime Date { get; set; }

    // Half trays allowed (e.g. 2.5 trays of adobo).
    public decimal TraysProduced { get; set; }
    public int ServingsPerTray { get; set; }
    public DateTime ProducedAt { get; set; }
}
