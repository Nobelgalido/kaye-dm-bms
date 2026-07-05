using KayeDM.Application.Inventory;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Inventory;

public class InventoryService : IInventoryService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public InventoryService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<DishBatchDto> CreateBatchAsync(CreateDishBatchRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var menuItem = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == request.MenuItemId)
            ?? throw new DomainException($"Menu item {request.MenuItemId} not found.");

        if (request.TraysProduced <= 0)
        {
            throw new DomainException("Trays produced must be positive.");
        }

        if (request.ServingsPerTray <= 0)
        {
            throw new DomainException("Servings per tray must be positive.");
        }

        var batch = new DishBatch
        {
            MenuItemId = request.MenuItemId,
            Date = DateTime.Now.Date,
            TraysProduced = request.TraysProduced,
            ServingsPerTray = request.ServingsPerTray,
            ProducedAt = DateTime.Now
        };

        db.DishBatches.Add(batch);
        await db.SaveChangesAsync();

        return new DishBatchDto(batch.Id, batch.MenuItemId, menuItem.Name, batch.Date, batch.TraysProduced, batch.ServingsPerTray, batch.ProducedAt);
    }

    public async Task<List<DishBatchDto>> GetTodaysBatchesAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var today = DateTime.Now.Date;
        return await db.DishBatches
            .AsNoTracking()
            .Include(b => b.MenuItem)
            .Where(b => b.Date == today)
            .OrderBy(b => b.MenuItem.Name)
            .Select(b => new DishBatchDto(b.Id, b.MenuItemId, b.MenuItem.Name, b.Date, b.TraysProduced, b.ServingsPerTray, b.ProducedAt))
            .ToListAsync();
    }

    public async Task<WasteLogDto> LogWasteAsync(LogWasteRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var batch = await db.DishBatches.Include(b => b.MenuItem).FirstOrDefaultAsync(b => b.Id == request.DishBatchId)
            ?? throw new DomainException($"Dish batch {request.DishBatchId} not found.");

        if (request.TraysWasted <= 0)
        {
            throw new DomainException("Trays wasted must be positive.");
        }

        var log = new WasteLog
        {
            DishBatchId = batch.Id,
            TraysWasted = request.TraysWasted,
            Reason = request.Reason,
            LoggedAt = DateTime.Now,
            LoggedById = request.LoggedById
        };

        db.WasteLogs.Add(log);
        await db.SaveChangesAsync();

        return new WasteLogDto(log.Id, batch.Id, batch.MenuItem.Name, log.TraysWasted, log.Reason, log.LoggedAt);
    }

    public async Task<List<MenuItemAvailabilityDto>> GetTodaysAvailabilityAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var today = DateTime.Now.Date;
        var menuItems = await db.MenuItems.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.SortOrder).ToListAsync();

        var result = new List<MenuItemAvailabilityDto>();
        foreach (var item in menuItems)
        {
            var hasBatch = await db.DishBatches.AnyAsync(b => b.MenuItemId == item.Id && b.Date == today);
            int? available = hasBatch ? await AvailabilityCalculator.GetAvailableServingsAsync(db, item.Id, today) : null;
            result.Add(new MenuItemAvailabilityDto(item.Id, item.Name, hasBatch, available));
        }

        return result;
    }

    public async Task<List<VarianceRow>> GetVarianceAsync(DateOnly from, DateOnly to)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var fromDate = from.ToDateTime(TimeOnly.MinValue);
        var toDate = to.ToDateTime(TimeOnly.MinValue).AddDays(1);

        var batches = await db.DishBatches
            .AsNoTracking()
            .Include(b => b.MenuItem)
            .Where(b => b.Date >= fromDate && b.Date < toDate)
            .ToListAsync();

        var rows = new List<VarianceRow>();
        foreach (var batch in batches)
        {
            var produced = await AvailabilityCalculator.GetProducedServingsAsync(db, batch.MenuItemId, batch.Date);
            var sold = await AvailabilityCalculator.GetSoldServingsAsync(db, batch.MenuItemId, batch.Date);
            var wasted = await AvailabilityCalculator.GetWastedServingsAsync(db, batch.MenuItemId, batch.Date);
            var variance = produced - sold - wasted;
            var variancePercent = produced == 0 ? 0m : Math.Round((decimal)variance / produced * 100m, 1);

            rows.Add(new VarianceRow(DateOnly.FromDateTime(batch.Date), batch.MenuItemId, batch.MenuItem.Name, produced, sold, wasted, variancePercent));
        }

        return rows
            .GroupBy(r => (r.Date, r.MenuItemId))
            .Select(g => g.First())
            .OrderBy(r => r.Date).ThenBy(r => r.MenuItemName)
            .ToList();
    }
}
