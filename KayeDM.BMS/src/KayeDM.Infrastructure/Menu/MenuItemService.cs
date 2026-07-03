using KayeDM.Application.Menu;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Menu;

public class MenuItemService : IMenuItemService
{
    private readonly AppDbContext _db;

    public MenuItemService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<MenuItemDto>> GetAllAsync(bool includeInactive = false)
    {
        var query = _db.MenuItems.AsNoTracking().AsQueryable();
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
        var menuItem = await _db.MenuItems.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        return menuItem is null
            ? null
            : new MenuItemDto(menuItem.Id, menuItem.Name, menuItem.Category, menuItem.Price, menuItem.IsActive, menuItem.SortOrder);
    }

    public async Task<MenuItemDto> CreateAsync(MenuItemUpsertDto dto)
    {
        var entity = new MenuItem
        {
            Name = dto.Name,
            Category = dto.Category,
            Price = dto.Price,
            SortOrder = dto.SortOrder,
            IsActive = true
        };

        _db.MenuItems.Add(entity);
        await _db.SaveChangesAsync();

        return new MenuItemDto(entity.Id, entity.Name, entity.Category, entity.Price, entity.IsActive, entity.SortOrder);
    }

    public async Task<MenuItemDto> UpdateAsync(int id, MenuItemUpsertDto dto)
    {
        var entity = await _db.MenuItems.FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new DomainException($"Menu item {id} not found.");

        entity.Name = dto.Name;
        entity.Category = dto.Category;
        entity.Price = dto.Price;
        entity.SortOrder = dto.SortOrder;

        await _db.SaveChangesAsync();

        return new MenuItemDto(entity.Id, entity.Name, entity.Category, entity.Price, entity.IsActive, entity.SortOrder);
    }

    public async Task SetActiveAsync(int id, bool isActive)
    {
        var entity = await _db.MenuItems.FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new DomainException($"Menu item {id} not found.");

        entity.IsActive = isActive;
        await _db.SaveChangesAsync();
    }
}
