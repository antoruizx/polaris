using Polaris.Domain.Devices;
using Polaris.Domain.Parking;

namespace Polaris.Domain.Tests;

public class ParkingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Hold = TimeSpan.FromMinutes(2);

    private static ParkingSpot Spot() => new("A-01", 1, 1, "esp32-park-01", 0, 0);

    [Fact]
    public void Arriving_car_waits_until_a_spot_is_assigned()
    {
        var reservation = Reservation.Arrive(1, T0);
        Assert.True(reservation.IsWaiting);
        Assert.Equal("Auto 1", reservation.Label);

        var spot = Spot();
        reservation.Assign(spot, T0, Hold);

        Assert.Equal(SpotState.Reserved, spot.StateAt(T0.AddSeconds(10), reservation));
    }

    [Fact]
    public void Reservation_stops_holding_the_spot_after_it_expires()
    {
        var spot = Spot();
        var reservation = Reservation.Arrive(1, T0);
        reservation.Assign(spot, T0, Hold);

        Assert.Equal(SpotState.Free, spot.StateAt(T0 + Hold + TimeSpan.FromSeconds(1), reservation));
        Assert.True(reservation.Expire(T0 + Hold));
        Assert.Equal(ReservationStatus.Expired, reservation.Status);
    }

    [Fact]
    public void Occupied_wins_over_reserved()
    {
        var spot = Spot();
        var reservation = Reservation.Arrive(1, T0);
        reservation.Assign(spot, T0, Hold);

        Assert.True(spot.ApplyOccupancy(true, 5.1, T0.AddSeconds(20)));
        Assert.Equal(SpotState.Occupied, spot.StateAt(T0.AddSeconds(20), reservation));
    }

    [Fact]
    public void Same_reading_twice_is_not_a_change()
    {
        var spot = Spot();
        Assert.True(spot.ApplyOccupancy(true, 5, T0));
        Assert.False(spot.ApplyOccupancy(true, 6, T0.AddSeconds(1)));
        Assert.Equal(6.0, spot.LastDistanceCm);
    }

    [Fact]
    public void Waiting_reservation_can_be_cancelled_but_not_fulfilled()
    {
        var reservation = Reservation.Arrive(3, T0);
        Assert.False(reservation.Fulfill(T0.AddSeconds(1)));
        Assert.True(reservation.Cancel(T0.AddSeconds(2)));
    }

    [Fact]
    public void Device_discards_duplicate_sequence_numbers_and_resets_on_reboot()
    {
        var device = new Device("esp32-park-01", DeviceKind.Parking, "Estacionamiento A");

        Assert.True(device.AcceptSequence("boot1", 1));
        Assert.True(device.AcceptSequence("boot1", 2));
        Assert.False(device.AcceptSequence("boot1", 2));
        Assert.True(device.AcceptSequence("boot2", 1)); // reinicio de la placa
    }
}
