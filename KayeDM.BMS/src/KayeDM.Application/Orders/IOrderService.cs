namespace KayeDM.Application.Orders;

public interface IOrderService
{
    Task<OrderResult> CreateOrderAsync(CreateOrderRequest request);
    Task<OrderResult> CreateCrewMealOrderAsync(CreateCrewMealOrderRequest request);
    Task VoidOrderAsync(int orderId, string reason);
}
