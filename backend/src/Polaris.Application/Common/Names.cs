using Polaris.Domain.Logistics;
using Polaris.Domain.Parking;

namespace Polaris.Application.Common;

/// <summary>Nombres estables (camelCase) que viajan en el JSON hacia la web.</summary>
public static class Names
{
    public static string Of(StockState s) => s switch
    {
        StockState.Ok => "ok",
        StockState.Low => "low",
        StockState.Out => "out",
        _ => s.ToString()
    };

    public static string Of(PickStatus s) => s switch
    {
        PickStatus.Queued => "queued",
        PickStatus.Lit => "lit",
        PickStatus.Confirmed => "confirmed",
        PickStatus.Cancelled => "cancelled",
        PickStatus.Expired => "expired",
        _ => s.ToString()
    };

    public static string Of(PickSource s) => s switch
    {
        PickSource.Web => "web",
        PickSource.Button => "button",
        _ => s.ToString()
    };

    public static string Of(MovementType t) => t switch
    {
        MovementType.Withdrawal => "withdrawal",
        MovementType.Restock => "restock",
        _ => t.ToString()
    };

    public static string Of(SpotState s) => s switch
    {
        SpotState.Free => "free",
        SpotState.Occupied => "occupied",
        SpotState.Reserved => "reserved",
        SpotState.OutOfService => "outOfService",
        _ => s.ToString()
    };

    public static string Of(ReservationStatus s) => s switch
    {
        ReservationStatus.Waiting => "waiting",
        ReservationStatus.Assigned => "assigned",
        ReservationStatus.Fulfilled => "fulfilled",
        ReservationStatus.Expired => "expired",
        ReservationStatus.Cancelled => "cancelled",
        _ => s.ToString()
    };
}
