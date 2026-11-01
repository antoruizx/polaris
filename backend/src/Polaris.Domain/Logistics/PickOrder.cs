using Polaris.Domain.Common;

namespace Polaris.Domain.Logistics;

public enum PickStatus
{
    /// <summary>Esperando turno: ya hay otro retiro encendido en la misma estantería.</summary>
    Queued,

    /// <summary>Luz encendida, esperando confirmación.</summary>
    Lit,

    Confirmed,
    Cancelled,
    Expired
}

public enum PickSource
{
    Web,
    Button
}

/// <summary>
/// Orden de picking. Se atiende por orden de llegada: una sola luz encendida por estantería,
/// las demás esperan en cola y se encienden cuando se cierra la anterior.
/// </summary>
public sealed class PickOrder
{
    private PickOrder() { }

    public static PickOrder Create(Product product, Compartment compartment, int quantity, DateTimeOffset now, bool light)
    {
        if (quantity < 1) throw DomainException.Validation("invalid_quantity", "La cantidad debe ser al menos 1.");

        var pick = new PickOrder
        {
            Id = Guid.NewGuid(),
            Product = product,
            ProductId = product.Id,
            Compartment = compartment,
            CompartmentId = compartment.Id,
            Quantity = quantity,
            Status = PickStatus.Queued,
            CreatedAt = now
        };

        if (light) pick.Light(now);
        return pick;
    }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public Guid CompartmentId { get; private set; }
    public Compartment Compartment { get; private set; } = null!;
    public int Quantity { get; private set; }
    public PickStatus Status { get; private set; }

    /// <summary>Cuándo se pidió (ordena la cola).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Cuándo se encendió la luz (empieza a correr el tiempo de búsqueda).</summary>
    public DateTimeOffset? LitAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }
    public int? ConfirmedQuantity { get; private set; }
    public PickSource? Source { get; private set; }

    /// <summary>Milisegundos desde que se encendió la luz hasta la confirmación.</summary>
    public long? DurationMs { get; private set; }

    public bool IsLit => Status == PickStatus.Lit;
    public bool IsOpen => Status is PickStatus.Queued or PickStatus.Lit;

    public void Light(DateTimeOffset now)
    {
        if (Status != PickStatus.Queued)
            throw DomainException.Conflict("pick_not_queued", "Este retiro no está en cola.");

        Status = PickStatus.Lit;
        LitAt = now;
    }

    /// <summary>
    /// Confirma el retiro. Devuelve false si ya estaba confirmado (confirmar dos veces no hace nada).
    /// </summary>
    public bool Confirm(int quantity, PickSource source, DateTimeOffset now)
    {
        if (Status == PickStatus.Confirmed) return false;
        if (!IsLit) throw DomainException.Conflict("pick_not_active", "Este retiro no tiene la luz encendida.");
        if (quantity < 1) throw DomainException.Validation("invalid_quantity", "La cantidad debe ser al menos 1.");

        Status = PickStatus.Confirmed;
        ConfirmedQuantity = quantity;
        Source = source;
        ClosedAt = now;
        DurationMs = (long)Math.Max(0, (now - LitAt!.Value).TotalMilliseconds);
        return true;
    }

    /// <summary>Saca el retiro de la cola o apaga su luz, sin descontar stock.</summary>
    public bool Cancel(DateTimeOffset now)
    {
        if (!IsOpen) return false;
        Status = PickStatus.Cancelled;
        ClosedAt = now;
        return true;
    }

    /// <summary>Apaga una luz que nadie confirmó a tiempo.</summary>
    public bool Expire(DateTimeOffset now)
    {
        if (!IsLit) return false;
        Status = PickStatus.Expired;
        ClosedAt = now;
        return true;
    }
}
