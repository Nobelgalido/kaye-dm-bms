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

            entity.HasMany(o => o.Lines)
                .WithOne(l => l.Order)
                .HasForeignKey(l => l.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OrderLine>(entity =>
        {
            entity.Property(l => l.UnitPriceAtSale).HasPrecision(10, 2);

            entity.HasOne(l => l.MenuItem)
                .WithMany()
                .HasForeignKey(l => l.MenuItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
