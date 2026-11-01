using Microsoft.AspNetCore.SignalR;
using Polaris.Application.Abstractions;
using Polaris.Application.Devices;
using Polaris.Application.Logistics;
using Polaris.Application.Parking;

namespace Polaris.Api.Realtime;

public sealed class SignalRNotifier(IHubContext<PolarisHub> hub) : IRealtimeNotifier
{
    public Task PickChanged(PickDto pick) => hub.Clients.Group("logistics").SendAsync("PickChanged", pick);

    public Task StockChanged(ProductDto product) => hub.Clients.Group("logistics").SendAsync("StockChanged", product);

    public Task SpotChanged(SpotDto spot) => hub.Clients.Group("parking").SendAsync("SpotChanged", spot);

    public Task ReservationChanged(ReservationDto reservation) =>
        hub.Clients.Group("parking").SendAsync("ReservationChanged", reservation);

    public Task DeviceStatusChanged(DeviceDto device) =>
        hub.Clients.Group("devices").SendAsync("DeviceStatusChanged", device);

    public Task DeviceTraffic(TrafficDto entry) => hub.Clients.Group("devices").SendAsync("DeviceTraffic", entry);
}
