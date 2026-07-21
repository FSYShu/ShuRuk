namespace ShuRuk.Runtime;

/// <summary>
/// Provides a managed execution boundary for module code.
/// This wraps module execution with crash isolation (exception catching) and
/// permission enforcement. It does NOT provide OS-level process/container sandboxing.
/// Module code runs in-process within the host application.
///
/// IMPORTANT: Permission checks via PermissionGuard are advisory only for in-process code
/// and do not provide true isolation or security guarantees. In-process module code can
/// potentially bypass these checks. For actual security isolation, modules must be executed
/// in a separate process or container with OS-level sandboxing.
/// </summary>
public class ModuleExecutionBoundary
{
    private readonly PermissionGuard _permissionGuard;
    private readonly string _moduleDataPath;

    /// <summary>
    /// Initializes a new managed execution boundary for a module.
    /// </summary>
    /// <param name="permissionGuard">The permission guard to enforce access control.</param>
    /// <param name="moduleDataPath">The constrained data path for module file operations.</param>
    public ModuleExecutionBoundary(PermissionGuard permissionGuard, string moduleDataPath)
    {
        _permissionGuard = permissionGuard;
        _moduleDataPath = moduleDataPath;
    }

    /// <summary>
    /// Indicates whether the module has faulted (thrown an unhandled exception) during execution.
    /// </summary>
    public bool HasFaulted { get; private set; }

    /// <summary>
    /// Executes a module action within the managed execution boundary.
    /// Catches unhandled exceptions and wraps them in a <see cref="ModuleExecutionException"/>.
    /// <see cref="UnauthorizedAccessException"/> is re-thrown directly.
    /// <see cref="OperationCanceledException"/> is preserved to support cancellation.
    ///
    /// Note: This method does not enforce permission checks during execution. Permission enforcement
    /// is advisory and relies on module code voluntarily using the PermissionGuard API. In-process
    /// module code can potentially bypass these checks.
    /// </summary>
    public async Task<TResult?> ExecuteAsync<TResult>(Func<CancellationToken, Task<TResult>> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await action(cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            HasFaulted = true;
            throw new ModuleExecutionException("Module execution failed within managed execution boundary", ex);
        }
    }

    /// <summary>
    /// Executes a void-returning module action within the managed execution boundary.
    /// </summary>
    public async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async ct =>
        {
            await action(ct);
            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Gets the <see cref="PermissionGuard"/> associated with this execution boundary.
    /// </summary>
    public PermissionGuard GetPermissionGuard() => _permissionGuard;

    /// <summary>
    /// Gets the constrained data path for this module's file operations.
    /// </summary>
    public string GetConstrainedDataPath() => _moduleDataPath;
}

/// <summary>
/// Represents an unhandled exception that occurred during module execution
/// within the managed execution boundary.
/// </summary>
public class ModuleExecutionException : Exception
{
    public ModuleExecutionException(string message, Exception innerException)
        : base(message, innerException) { }
}