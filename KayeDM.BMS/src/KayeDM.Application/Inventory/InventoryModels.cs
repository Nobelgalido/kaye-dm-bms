using KayeDM.Domain.Enums;

namespace KayeDM.Application.Inventory;

public record CreateDishBatchRequest(int MenuItemId, decimal TraysProduced, int ServingsPerTray);

public record DishBatchDto(
    int Id,
    int MenuItemId,
    string MenuItemName,
    DateTime Date,
    decimal TraysProduced,
    int ServingsPerTray,
    DateTime ProducedAt);

public record LogWasteRequest(int DishBatchId, decimal TraysWasted, WasteReason Reason);

public record WasteLogDto(
    int Id,
    int DishBatchId,
    string MenuItemName,
    decimal TraysWasted,
    WasteReason Reason,
    DateTime LoggedAt);

public record MenuItemAvailabilityDto(int MenuItemId, string MenuItemName, bool HasBatchToday, int? AvailableServings);

// Variance = Produced - Sold - Wasted (the same "available" formula, viewed as
// an end-of-range accounting check rather than a live POS number — a
// well-run day should land near zero; negative means the day oversold).
public record VarianceRow(
    DateOnly Date,
    int MenuItemId,
    string MenuItemName,
    int Produced,
    int Sold,
    int Wasted,
    decimal VariancePercent);
