using KayeDM.Application.Orders;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Orders;

public class OrderService : IOrderService
{
    private readonly AppDbContext _db;

    public OrderService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<OrderResult> CreateOrderAsync(CreateOrderRequest request)
    {
        if (request.Lines is null || request.Lines.Count == 0)
        {
            throw new DomainException("An order must have at least one line.");
        }

        var menuItemIds = request.Lines.Select(l => l.MenuItemId).Distinct().ToList();
        var menuItems = await _db.MenuItems
            .Where(m => menuItemIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id);

        var order = new Order
        {
            OrderNumber = await NextOrderNumberAsync(),
            CreatedAt = DateTime.Now,
            CashierId = request.CashierId,
            Status = OrderStatus.Completed,
            PaymentMethod = request.PaymentMethod
        };

        decimal total = 0m;
        var lineResults = new List<OrderLineResult>();

        foreach (var lineRequest in request.Lines)
        {
            if (!menuItems.TryGetValue(lineRequest.MenuItemId, out var menuItem))
            {
                throw new DomainException($"Menu item {lineRequest.MenuItemId} not found.");
            }

            if (lineRequest.Quantity <= 0)
            {
                throw new DomainException("Line quantity must be positive.");
            }

            var unitPrice = menuItem.Price;
            var lineTotal = unitPrice * lineRequest.Quantity;
            total += lineTotal;

            order.Lines.Add(new OrderLine
            {
                MenuItemId = menuItem.Id,
                Quantity = lineRequest.Quantity,
                UnitPriceAtSale = unitPrice
            });

            lineResults.Add(new OrderLineResult(menuItem.Id, menuItem.Name, lineRequest.Quantity, unitPrice, lineTotal));
        }

        if (request.AmountTendered < total)
        {
            throw new DomainException(
                $"Amount tendered ({request.AmountTendered:N2}) is less than the order total ({total:N2}).");
        }

        order.AmountTendered = request.AmountTendered;
        order.ChangeGiven = request.AmountTendered - total;

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        return new OrderResult(order.Id, order.OrderNumber, order.CreatedAt, total, order.AmountTendered, order.ChangeGiven, lineResults);
    }

    public async Task VoidOrderAsync(int orderId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A void reason is required.");
        }

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new DomainException($"Order {orderId} not found.");

        if (order.Status == OrderStatus.Voided)
        {
            throw new DomainException($"Order {orderId} is already voided.");
        }

        order.Status = OrderStatus.Voided;
        order.VoidReason = reason;
        await _db.SaveChangesAsync();
    }

    private async Task<string> NextOrderNumberAsync()
    {
        var today = DateTime.Now.Date;
        var countToday = await _db.Orders.CountAsync(o => o.CreatedAt >= today && o.CreatedAt < today.AddDays(1));
        return $"{today:yyyyMMdd}-{countToday + 1:D3}";
    }
}
