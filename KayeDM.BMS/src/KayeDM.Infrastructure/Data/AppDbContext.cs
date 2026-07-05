using KayeDM.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Data;

public class AppDbContext : IdentityDbContext<IdentityUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<BusCompany> BusCompanies => Set<BusCompany>();
    public DbSet<BusTrip> BusTrips => Set<BusTrip>();
    public DbSet<CrewMealCredit> CrewMealCredits => Set<CrewMealCredit>();
    public DbSet<DishBatch> DishBatches => Set<DishBatch>();
    public DbSet<WasteLog> WasteLogs => Set<WasteLog>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<DailyClosing> DailyClosings => Set<DailyClosing>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MenuItem>(entity =>
        {
            entity.Property(m => m.Name).HasMaxLength(100).IsRequired();
            entity.Property(m => m.Price).HasPrecision(10, 2);
        });

        builder.Entity<Order>(entity =>
        {
            entity.Property(o => o.OrderNumber).HasMaxLength(20).IsRequired();
            entity.HasIndex(o => o.OrderNumber).IsUnique();
            entity.Property(o => o.CashierId).HasMaxLength(450);
            entity.Property(o => o.AmountTendered).HasPrecision(10, 2);
            entity.Property(o => o.ChangeGiven).HasPrecision(10, 2);
            entity.Property(o => o.VoidReason).HasMaxLength(250);
            entity.Property(o => o.OversoldOverride).HasDefaultValue(false);

            entity.HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .HasForeignKey(l => l.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(o => o.BusTrip)
                .WithMany()
                .HasForeignKey(o => o.BusTripId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<OrderLine>(entity =>
        {
            entity.Property(l => l.UnitPriceAtSale).HasPrecision(10, 2);

            entity.HasOne(l => l.MenuItem)
                .WithMany()
                .HasForeignKey(l => l.MenuItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<BusCompany>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(150).IsRequired();
            entity.Property(c => c.ContactPerson).HasMaxLength(150);
        });

        builder.Entity<BusTrip>(entity =>
        {
            entity.Property(t => t.BusNumber).HasMaxLength(50).IsRequired();
            entity.Property(t => t.Route).HasMaxLength(200).IsRequired();

            entity.HasOne(t => t.BusCompany)
                .WithMany()
                .HasForeignKey(t => t.BusCompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CrewMealCredit>(entity =>
        {
            entity.HasOne(c => c.BusTrip)
                .WithMany()
                .HasForeignKey(c => c.BusTripId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.Order)
                .WithMany()
                .HasForeignKey(c => c.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            // One credit per ₱0 crew-meal order — enforced at the DB level too.
            entity.HasIndex(c => c.OrderId).IsUnique();
        });

        builder.Entity<DishBatch>(entity =>
        {
            entity.Property(b => b.TraysProduced).HasPrecision(10, 2);

            entity.HasOne(b => b.MenuItem)
                .WithMany()
                .HasForeignKey(b => b.MenuItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<WasteLog>(entity =>
        {
            entity.Property(w => w.TraysWasted).HasPrecision(10, 2);
            entity.Property(w => w.LoggedById).HasMaxLength(450);

            entity.HasOne(w => w.DishBatch)
                .WithMany()
                .HasForeignKey(w => w.DishBatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ExpenseCategory>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(100).IsRequired();
        });

        builder.Entity<Expense>(entity =>
        {
            entity.Property(e => e.Description).HasMaxLength(250).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(10, 2);
            entity.Property(e => e.Vendor).HasMaxLength(150);
            entity.Property(e => e.ReceiptRef).HasMaxLength(100);
            entity.Property(e => e.LoggedById).HasMaxLength(450);

            entity.HasOne(e => e.ExpenseCategory)
                .WithMany()
                .HasForeignKey(e => e.ExpenseCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<DailyClosing>(entity =>
        {
            entity.Property(c => c.TotalSales).HasPrecision(10, 2);
            entity.Property(c => c.CashSales).HasPrecision(10, 2);
            entity.Property(c => c.GCashSales).HasPrecision(10, 2);
            entity.Property(c => c.TotalExpenses).HasPrecision(10, 2);
            entity.Property(c => c.NetForDay).HasPrecision(10, 2);
            entity.Property(c => c.ClosedById).HasMaxLength(450);

            entity.HasIndex(c => c.Date).IsUnique();
        });
    }
}
