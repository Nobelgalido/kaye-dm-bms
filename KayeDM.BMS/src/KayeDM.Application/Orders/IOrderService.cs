namespace KayeDM.Application.Orders;

public interface IOrderService
{
    Task<OrderResult> CreateOrderAsync(CreateOrderRequest request);
    Task VoidOrderAsync(int orderId, string reason);
}
