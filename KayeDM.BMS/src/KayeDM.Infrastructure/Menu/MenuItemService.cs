using KayeDM.Application.Menu;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Menu;

public class MenuItemService : IMenuItemService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public MenuItemService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<List<MenuItemDto>> GetAllAsync(bool includeInactive = false)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var query = db.MenuItems.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(m => m.IsActive);
        }

        return await query
            .OrderBy(m => m.SortOrder)
            .Select(m => new MenuItemDto(m.Id, m.Name, m.Category, m.Price, m.IsActive, m.SortOrder))
            .ToListAsync();
    }

    public async Task<MenuItemDto?> GetByIdAsync(int id)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var menuItem = await db.MenuItems.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        return menuItem is null
            ? null
            : new MenuItemDto(menuItem.Id, menuItem.Name, menuItem.Category, menuItem.Price, menuItem.IsActive, menuItem.SortOrder);
    }

    public async Task<MenuItemDto> CreateAsync(MenuItemUpsertDto dto)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = new MenuItem
        {
            Name = dto.Name,
            Category = dto.Category,
            Price = dto.Price,
            SortOrder = dto.SortOrder,
            IsActive = true
        };

        db.MenuItems.Add(entity);
        await db.SaveChangesAsync();

        return new MenuItemDto(entity.Id, entity.Name, entity.Category, entity.Price, entity.IsActive, entity.SortOrder);
    }

    public async Task<MenuItemDto> UpdateAsync(int id, MenuItemUpsertDto dto)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new DomainException($"Menu item {id} not found.");

        entity.Name = dto.Name;
        entity.Category = dto.Category;
        entity.Price = dto.Price;
        entity.SortOrder = dto.SortOrder;

        await db.SaveChangesAsync();

        return new MenuItemDto(entity.Id, entity.Name, entity.Category, entity.Price, entity.IsActive, entity.SortOrder);
    }

    public async Task SetActiveAsync(int id, bool isActive)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new DomainException($"Menu item {id} not found.");

        entity.IsActive = isActive;
        await db.SaveChangesAsync();
    }
}
