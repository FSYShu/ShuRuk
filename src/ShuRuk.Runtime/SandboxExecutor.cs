namespace ShuRuk.Runtime;

public class SandboxExecutor
{
    private readonly PermissionGuard _permissionGuard;
    private readonly string _moduleDataPath;

    public SandboxExecutor(PermissionGuard permissionGuard, string moduleDataPath)
    {
        _permissionGuard = permissionGuard;
        _moduleDataPath = moduleDataPath;
    }

    public bool IsIsolated { get; private set; }

    public async Task<TResult?> ExecuteAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            IsIsolated = true;
            throw new ModuleExecutionException("Module execution failed within sandbox boundary", ex);
        }
    }

    public async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async () =>
        {
            await action();
            return true;
        }, cancellationToken);
    }

    public PermissionGuard GetPermissionGuard() => _permissionGuard;

    public string GetConstrainedDataPath() => _moduleDataPath;
}

public class ModuleExecutionException : Exception
{
    public ModuleExecutionException(string message, Exception innerException)
        : base(message, innerException) { }
}