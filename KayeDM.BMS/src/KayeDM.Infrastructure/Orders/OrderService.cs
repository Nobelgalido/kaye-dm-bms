using KayeDM.Application.Orders;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Orders;

public class OrderService : IOrderService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public OrderService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<OrderResult> CreateOrderAsync(CreateOrderRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        if (request.Lines is null || request.Lines.Count == 0)
        {
            throw new DomainException("An order must have at least one line.");
        }

        var menuItemIds = request.Lines.Select(l => l.MenuItemId).Distinct().ToList();
        var menuItems = await db.MenuItems
            .Where(m => menuItemIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id);

        var order = new Order
        {
            OrderNumber = await NextOrderNumberAsync(db),
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

        if (request.PaymentMethod == PaymentMethod.GCash)
        {
            // Digital payment: there is no "tendered cash" or "change." The server is
            // authoritative here and ignores whatever tendered amount the client sent.
            order.AmountTendered = total;
            order.ChangeGiven = 0m;
        }
        else
        {
            if (request.AmountTendered < total)
            {
                throw new DomainException(
                    $"Amount tendered ({request.AmountTendered:N2}) is less than the order total ({total:N2}).");
            }

            order.AmountTendered = request.AmountTendered;
            order.ChangeGiven = request.AmountTendered - total;
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return new OrderResult(order.Id, order.OrderNumber, order.CreatedAt, total, order.AmountTendered, order.ChangeGiven, lineResults);
    }

    public async Task VoidOrderAsync(int orderId, string reason)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A void reason is required.");
        }

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new DomainException($"Order {orderId} not found.");

        if (order.Status == OrderStatus.Voided)
        {
            throw new DomainException($"Order {orderId} is already voided.");
        }

        order.Status = OrderStatus.Voided;
        order.VoidReason = reason;
        await db.SaveChangesAsync();
    }

    private static async Task<string> NextOrderNumberAsync(AppDbContext db)
    {
        var today = DateTime.Now.Date;
        var countToday = await db.Orders.CountAsync(o => o.CreatedAt >= today && o.CreatedAt < today.AddDays(1));
        return $"{today:yyyyMMdd}-{countToday + 1:D3}";
    }
}
