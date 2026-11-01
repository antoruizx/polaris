using Polaris.Domain.Common;
using Polaris.Domain.Logistics;

namespace Polaris.Domain.Tests;

public class PickOrderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 15, 0, 0, TimeSpan.Zero);

    private static (Product, Compartment) Sample()
    {
        var product = new Product("PL-007", "Cutter profesional 18 mm", "Herramientas", 4);
        var compartment = new Compartment("B", 1, 7, "esp32-log-01", 6);
        return (product, compartment);
    }

    [Fact]
    public void Lit_pick_measures_time_from_light_to_confirmation()
    {
        var (product, compartment) = Sample();
        var pick = PickOrder.Create(product, compartment, 1, T0, light: true);

        Assert.True(pick.Confirm(1, PickSource.Button, T0.AddSeconds(4.5)));
        Assert.Equal(PickStatus.Confirmed, pick.Status);
        Assert.Equal(4500L, pick.DurationMs);
    }

    [Fact]
    public void Confirming_twice_is_a_no_op()
    {
        var (product, compartment) = Sample();
        var pick = PickOrder.Create(product, compartment, 1, T0, light: true);

        Assert.True(pick.Confirm(1, PickSource.Web, T0.AddSeconds(2)));
        Assert.False(pick.Confirm(1, PickSource.Button, T0.AddSeconds(3)));
        Assert.Equal(PickSource.Web, pick.Source);
    }

    [Fact]
    public void Queued_pick_cannot_be_confirmed_until_lit()
    {
        var (product, compartment) = Sample();
        var pick = PickOrder.Create(product, compartment, 1, T0, light: false);

        var ex = Assert.Throws<DomainException>(() => pick.Confirm(1, PickSource.Web, T0.AddSeconds(1)));
        Assert.Equal("pick_not_active", ex.Code);

        pick.Light(T0.AddSeconds(10));
        Assert.True(pick.Confirm(1, PickSource.Web, T0.AddSeconds(12)));
        Assert.Equal(2000L, pick.DurationMs); // cuenta desde que se encendió, no desde que se pidió
    }

    [Fact]
    public void Only_lit_picks_expire()
    {
        var (product, compartment) = Sample();
        var queued = PickOrder.Create(product, compartment, 1, T0, light: false);
        var lit = PickOrder.Create(product, compartment, 1, T0, light: true);

        Assert.False(queued.Expire(T0.AddMinutes(5)));
        Assert.True(lit.Expire(T0.AddMinutes(5)));
        Assert.Equal(PickStatus.Expired, lit.Status);
    }

    [Fact]
    public void Withdraw_more_than_available_is_rejected()
    {
        var (product, compartment) = Sample();
        var item = new StockItem(product, compartment, 2);

        var ex = Assert.Throws<DomainException>(() => item.Withdraw(3));
        Assert.Equal("insufficient_stock", ex.Code);
        Assert.Equal(2, item.Quantity);
    }

    [Theory]
    [InlineData(0, 5, StockState.Out)]
    [InlineData(3, 5, StockState.Low)]
    [InlineData(5, 5, StockState.Ok)]
    public void Stock_state_follows_minimum(int stock, int min, StockState expected) =>
        Assert.Equal(expected, StockStates.Of(stock, min));

    [Fact]
    public void Compartment_code_is_rack_level_slot()
    {
        var c = new Compartment("b", 2, 3, "esp32-log-01", 10);
        Assert.Equal("B-02-03", c.Code);
    }
}
