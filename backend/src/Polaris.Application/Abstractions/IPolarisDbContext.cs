using Microsoft.EntityFrameworkCore;
using Polaris.Domain.Devices;
using Polaris.Domain.Logistics;
using Polaris.Domain.Parking;

namespace Polaris.Application.Abstractions;

public interface IPolarisDbContext
{
    DbSet<Product> Products { get; }
    DbSet<Compartment> Compartments { get; }
    DbSet<StockItem> StockItems { get; }
    DbSet<PickOrder> PickOrders { get; }
    DbSet<InventoryMovement> InventoryMovements { get; }
    DbSet<ParkingSpot> ParkingSpots { get; }
    DbSet<Reservation> Reservations { get; }
    DbSet<OccupancyEvent> OccupancyEvents { get; }
    DbSet<Device> Devices { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
