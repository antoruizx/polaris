using Microsoft.EntityFrameworkCore;
using Polaris.Application.Abstractions;
using Polaris.Domain.Devices;
using Polaris.Domain.Logistics;
using Polaris.Domain.Parking;

namespace Polaris.Infrastructure.Persistence;

public sealed class PolarisDbContext(DbContextOptions<PolarisDbContext> options) : DbContext(options), IPolarisDbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Compartment> Compartments => Set<Compartment>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<PickOrder> PickOrders => Set<PickOrder>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<ParkingSpot> ParkingSpots => Set<ParkingSpot>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<OccupancyEvent> OccupancyEvents => Set<OccupancyEvent>();
    public DbSet<Device> Devices => Set<Device>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ---------- Dispositivos ----------
        b.Entity<Device>(e =>
        {
            e.ToTable("devices");
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).HasMaxLength(64);
            e.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(d => d.Name).HasMaxLength(120);
            e.Property(d => d.Firmware).HasMaxLength(40);
            e.Property(d => d.Ip).HasMaxLength(64);
            e.Property(d => d.BootId).HasMaxLength(64);
        });

        // ---------- Logística ----------
        b.Entity<Product>(e =>
        {
            e.ToTable("products");
            e.HasKey(p => p.Id);
            e.Property(p => p.Sku).HasMaxLength(32);
            e.HasIndex(p => p.Sku).IsUnique();
            e.Property(p => p.Name).HasMaxLength(200);
            e.Property(p => p.Category).HasMaxLength(80);
            e.Ignore(p => p.TotalStock);
            e.Ignore(p => p.StockState);
            e.HasMany(p => p.Stock).WithOne(s => s.Product).HasForeignKey(s => s.ProductId);
        });

        b.Entity<Compartment>(e =>
        {
            e.ToTable("compartments");
            e.HasKey(c => c.Id);
            e.Property(c => c.Code).HasMaxLength(16);
            e.HasIndex(c => c.Code).IsUnique();
            e.Property(c => c.Rack).HasMaxLength(4);
            e.Property(c => c.DeviceId).HasMaxLength(64);
            e.HasIndex(c => new { c.DeviceId, c.LedIndex }).IsUnique();
        });

        b.Entity<StockItem>(e =>
        {
            e.ToTable("stock_items");
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.ProductId, s.CompartmentId }).IsUnique();
            e.HasOne(s => s.Compartment).WithMany().HasForeignKey(s => s.CompartmentId);
        });

        b.Entity<PickOrder>(e =>
        {
            e.ToTable("pick_orders");
            e.HasKey(p => p.Id);
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.Source).HasConversion<string>().HasMaxLength(20);
            e.Ignore(p => p.IsLit);
            e.Ignore(p => p.IsOpen);
            e.HasIndex(p => p.Status);
            e.HasOne(p => p.Product).WithMany().HasForeignKey(p => p.ProductId);
            e.HasOne(p => p.Compartment).WithMany().HasForeignKey(p => p.CompartmentId);
        });

        b.Entity<InventoryMovement>(e =>
        {
            e.ToTable("inventory_movements");
            e.HasKey(m => m.Id);
            e.Property(m => m.Type).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(m => m.CreatedAt);
            e.HasOne(m => m.Product).WithMany().HasForeignKey(m => m.ProductId);
            e.HasOne(m => m.Compartment).WithMany().HasForeignKey(m => m.CompartmentId);
            e.HasOne(m => m.PickOrder).WithMany().HasForeignKey(m => m.PickOrderId).IsRequired(false);
        });

        // ---------- Parking ----------
        b.Entity<ParkingSpot>(e =>
        {
            e.ToTable("parking_spots");
            e.HasKey(s => s.Id);
            e.Property(s => s.Code).HasMaxLength(16);
            e.HasIndex(s => s.Code).IsUnique();
            e.Property(s => s.DeviceId).HasMaxLength(64);
            e.HasIndex(s => new { s.DeviceId, s.SensorChannel }).IsUnique();
        });

        b.Entity<Reservation>(e =>
        {
            e.ToTable("reservations");
            e.HasKey(r => r.Id);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            e.Ignore(r => r.Label);
            e.Ignore(r => r.IsWaiting);
            e.HasIndex(r => r.Status);
            e.HasOne(r => r.Spot).WithMany().HasForeignKey(r => r.SpotId).IsRequired(false);
        });

        b.Entity<OccupancyEvent>(e =>
        {
            e.ToTable("occupancy_events");
            e.HasKey(o => o.Id);
            e.HasIndex(o => o.At);
            e.HasOne(o => o.Spot).WithMany().HasForeignKey(o => o.SpotId);
        });
    }
}
