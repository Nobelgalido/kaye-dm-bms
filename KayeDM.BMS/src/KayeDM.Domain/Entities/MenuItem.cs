using KayeDM.Domain.Enums;

namespace KayeDM.Domain.Entities;

public class MenuItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public MenuCategory Category { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;

    // Drives POS grid layout — best-sellers top-left.
    public int SortOrder { get; set; }
}
