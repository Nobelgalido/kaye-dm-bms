namespace KayeDM.Application.Menu;

public interface IMenuItemService
{
    Task<List<MenuItemDto>> GetAllAsync(bool includeInactive = false);
    Task<MenuItemDto?> GetByIdAsync(int id);
    Task<MenuItemDto> CreateAsync(MenuItemUpsertDto dto);
    Task<MenuItemDto> UpdateAsync(int id, MenuItemUpsertDto dto);
    Task SetActiveAsync(int id, bool isActive);
}
