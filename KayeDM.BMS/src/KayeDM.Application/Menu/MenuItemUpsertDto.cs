using KayeDM.Domain.Enums;

namespace KayeDM.Application.Menu;

public record MenuItemUpsertDto(string Name, MenuCategory Category, decimal Price, int SortOrder);
