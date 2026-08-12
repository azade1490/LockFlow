using Microsoft.EntityFrameworkCore;
using LockFlow.OrderService.Domain.Order.AggregateRoot;
using LockFlow.OrderService.Domain.Order.Entities;

using System.Reflection;

namespace LockFlow.OrderService.Persistence.Data;
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions dbContextOptions)
        : base(dbContextOptions)
    {
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        
        modelBuilder.Entity<Order>()
    .Ignore(x => x.TotalAmount);

        modelBuilder.Entity<Order>()
    .OwnsOne(x => x.ShippingAddress, b =>
    {
        b.Property(x => x.Country);
        b.Property(x => x.Province);
        b.Property(x => x.City);
        b.Property(x => x.Street);
        b.Property(x => x.PostalCode);
    });

        modelBuilder.Entity<OrderItem>()
    .OwnsOne(x => x.UnitPrice, b =>
    {
        b.Property(x => x.Amount);
        b.Property(x => x.Currency);
    });

    }
    public DbSet<Order> Orders => Set<Order>();
}
