namespace KayeDM.Application.Inventory;

public interface IInventoryService
{
    Task<DishBatchDto> CreateBatchAsync(CreateDishBatchRequest request);
    Task<List<DishBatchDto>> GetTodaysBatchesAsync();
    Task<WasteLogDto> LogWasteAsync(LogWasteRequest request);

    // Feeds the POS availability strip: every active menu item, whether a
    // batch was logged today, and remaining servings if so (null = no batch,
    // meaning the item has no tracked limit today but remains sellable).
    Task<List<MenuItemAvailabilityDto>> GetTodaysAvailabilityAsync();

    Task<List<VarianceRow>> GetVarianceAsync(DateOnly from, DateOnly to);
}
