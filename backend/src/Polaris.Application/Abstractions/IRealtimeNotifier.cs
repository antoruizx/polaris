using Polaris.Application.Devices;
using Polaris.Application.Logistics;
using Polaris.Application.Parking;

namespace Polaris.Application.Abstractions;

/// <summary>
/// Empuja cambios a la web. Cada evento lleva el objeto completo actualizado, no un diff.
/// La implementación vive en la API (SignalR).
/// </summary>
public interface IRealtimeNotifier
{
    Task PickChanged(PickDto pick);
    Task StockChanged(ProductDto product);
    Task SpotChanged(SpotDto spot);
    Task ReservationChanged(ReservationDto reservation);
    Task DeviceStatusChanged(DeviceDto device);
    Task DeviceTraffic(TrafficDto entry);
}
