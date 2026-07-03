namespace KayeDM.Domain.Entities;

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;
    public int Quantity { get; set; }

    // Snapshot at sale time — menu price edits must never rewrite history.
    public decimal UnitPriceAtSale { get; set; }
}
