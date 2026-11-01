namespace Polaris.Application.Common;

/// <summary>
/// Serializa los comandos que cambian estado. Polaris corre en un solo proceso, así que esto alcanza
/// para que el botón físico y la web no confirmen el mismo retiro a la vez.
/// </summary>
public sealed class CommandGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<T> Run<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            return await action();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task Run(Func<Task> action, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            await action();
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
