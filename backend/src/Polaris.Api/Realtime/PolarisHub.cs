using Microsoft.AspNetCore.SignalR;

namespace Polaris.Api.Realtime;

/// <summary>
/// Hub de tiempo real para la web. El cliente se suscribe a los grupos que muestra:
/// "logistics", "parking" y "devices".
/// </summary>
public sealed class PolarisHub : Hub
{
    public static readonly string[] AllowedGroups = ["logistics", "parking", "devices"];

    public Task Subscribe(string group)
    {
        if (!AllowedGroups.Contains(group)) throw new HubException($"Grupo desconocido: {group}");
        return Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    public Task Unsubscribe(string group)
    {
        if (!AllowedGroups.Contains(group)) throw new HubException($"Grupo desconocido: {group}");
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }
}
