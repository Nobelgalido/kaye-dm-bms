using KayeDM.Domain.Enums;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Inventory;

// Shared by InventoryService (its own short-lived context, for the POS strip
// and variance report) and OrderService (the context it's about to insert an
// order into, for the oversell check) — one formula, one place.
public static class AvailabilityCalculator
{
    public static async Task<int> GetProducedServingsAsync(AppDbContext db, int menuItemId, DateTime date)
    {
        var batches = await db.DishBatches
            .Where(b => b.MenuItemId == menuItemId && b.Date == date)
            .Select(b => new { b.TraysProduced, b.ServingsPerTray })
            .ToListAsync();

        return (int)batches.Sum(b => b.TraysProduced * b.ServingsPerTray);
    }

    // "Sold" counts every completed order's lines for this item on this date —
    // voided orders are excluded, crew-meal orders are included (the driver
    // still ate the food; a crew-meal order is priced at ₱0, it is not voided).
    public static async Task<int> GetSoldServingsAsync(AppDbContext db, int menuItemId, DateTime date)
    {
        var nextDay = date.AddDays(1);

        return await db.OrderLines
            .Where(l => l.MenuItemId == menuItemId
                && l.Order.Status == OrderStatus.Completed
                && l.Order.CreatedAt >= date && l.Order.CreatedAt < nextDay)
            .SumAsync(l => (int?)l.Quantity) ?? 0;
    }

    public static async Task<int> GetWastedServingsAsync(AppDbContext db, int menuItemId, DateTime date)
    {
        var wastedServings = await db.WasteLogs
            .Where(w => w.DishBatch.MenuItemId == menuItemId && w.DishBatch.Date == date)
            .Select(w => w.TraysWasted * w.DishBatch.ServingsPerTray)
            .ToListAsync();

        return (int)wastedServings.Sum();
    }

    public static async Task<int> GetAvailableServingsAsync(AppDbContext db, int menuItemId, DateTime date)
    {
        var produced = await GetProducedServingsAsync(db, menuItemId, date);
        var sold = await GetSoldServingsAsync(db, menuItemId, date);
        var wasted = await GetWastedServingsAsync(db, menuItemId, date);

        return produced - sold - wasted;
    }
}
