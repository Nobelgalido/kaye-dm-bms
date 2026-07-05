using KayeDM.Application.Orders;
using KayeDM.Domain.Entities;
using KayeDM.Domain.Enums;
using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Closing;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Inventory;
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

        await ClosingGuard.EnsureDateNotClosedAsync(db, DateTime.Now.Date, "create an order");

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
            PaymentMethod = request.PaymentMethod,
            BusTripId = request.BusTripId
        };

        decimal total = 0m;
        var lineResults = new List<OrderLineResult>();
        var today = DateTime.Now.Date;
        var wasOversold = false;

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

            var hasBatchToday = await db.DishBatches.AnyAsync(b => b.MenuItemId == menuItem.Id && b.Date == today);
            if (hasBatchToday)
            {
                var available = await AvailabilityCalculator.GetAvailableServingsAsync(db, menuItem.Id, today);
                if (lineRequest.Quantity > available)
                {
                    if (!request.OversoldOverride)
                    {
                        throw new OversoldException(
                            $"Selling {lineRequest.Quantity} of {menuItem.Name} exceeds available servings ({available} left). Confirm to sell anyway.");
                    }

                    wasOversold = true;
                }
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

        order.OversoldOverride = wasOversold;

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

    public async Task<OrderResult> CreateCrewMealOrderAsync(CreateCrewMealOrderRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        await ClosingGuard.EnsureDateNotClosedAsync(db, DateTime.Now.Date, "create a crew meal order");

        if (request.Lines is null || request.Lines.Count == 0)
        {
            throw new DomainException("A crew meal order must have at least one line.");
        }

        var trip = await db.BusTrips.Include(t => t.BusCompany).FirstOrDefaultAsync(t => t.Id == request.BusTripId)
            ?? throw new DomainException($"Bus trip {request.BusTripId} not found.");

        var creditsUsed = await db.CrewMealCredits.CountAsync(c => c.BusTripId == request.BusTripId);
        if (creditsUsed >= trip.BusCompany.CrewMealAllowancePerTrip)
        {
            throw new DomainException(
                $"Crew meal allowance exceeded for this trip ({creditsUsed} of {trip.BusCompany.CrewMealAllowancePerTrip} used).");
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
            PaymentMethod = PaymentMethod.Cash,
            BusTripId = request.BusTripId,
            IsCrewMeal = true,
            AmountTendered = 0m,
            ChangeGiven = 0m
        };

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

            // Crew meals are free — the line is recorded for stock/traceability but
            // priced at zero so the order total is always ₱0.
            order.Lines.Add(new OrderLine
            {
                MenuItemId = menuItem.Id,
                Quantity = lineRequest.Quantity,
                UnitPriceAtSale = 0m
            });

            lineResults.Add(new OrderLineResult(menuItem.Id, menuItem.Name, lineRequest.Quantity, 0m, 0m));
        }

        // Order and credit are added to the same context and saved once: EF fixes up
        // CrewMealCredit.OrderId from Order.Id during this single SaveChangesAsync,
        // making the two inserts atomic.
        db.Orders.Add(order);
        db.CrewMealCredits.Add(new CrewMealCredit
        {
            BusTripId = request.BusTripId,
            CrewRole = request.CrewRole,
            Order = order,
            LoggedAt = DateTime.Now
        });

        await db.SaveChangesAsync();

        return new OrderResult(order.Id, order.OrderNumber, order.CreatedAt, 0m, 0m, 0m, lineResults);
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

        await ClosingGuard.EnsureDateNotClosedAsync(db, order.CreatedAt.Date, "void this order");

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
