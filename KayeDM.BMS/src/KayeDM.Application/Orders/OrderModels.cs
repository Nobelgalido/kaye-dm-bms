using KayeDM.Domain.Enums;

namespace KayeDM.Application.Orders;

public record OrderLineRequest(int MenuItemId, int Quantity);

public record CreateOrderRequest(
    IReadOnlyList<OrderLineRequest> Lines,
    PaymentMethod PaymentMethod,
    decimal AmountTendered,
    string? CashierId,
    int? BusTripId = null,
    bool OversoldOverride = false);

public record CreateCrewMealOrderRequest(
    IReadOnlyList<OrderLineRequest> Lines,
    int BusTripId,
    CrewRole CrewRole,
    string? CashierId);

public record OrderLineResult(int MenuItemId, string MenuItemName, int Quantity, decimal UnitPriceAtSale, decimal LineTotal);

public record OrderResult(
    int Id,
    string OrderNumber,
    DateTime CreatedAt,
    decimal Total,
    decimal AmountTendered,
    decimal ChangeGiven,
    IReadOnlyList<OrderLineResult> Lines);
