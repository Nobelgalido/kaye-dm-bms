using KayeDM.Domain.Enums;

namespace KayeDM.Application.Menu;

public record MenuItemDto(int Id, string Name, MenuCategory Category, decimal Price, bool IsActive, int SortOrder);
