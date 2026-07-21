namespace ShuRuk.Runtime;

public class IsolationBoundary
{
    private readonly Dictionary<string, ModuleExecutionBoundary> _executors = new();
    private readonly Dictionary<string, Exception?> _crashRecords = new();

    public ModuleExecutionBoundary CreateExecutor(string moduleName, PermissionGuard permissionGuard, string moduleDataPath)
    {
        var executor = new ModuleExecutionBoundary(permissionGuard, moduleDataPath);
        _executors[moduleName] = executor;
        _crashRecords.Remove(moduleName);
        return executor;
    }

    public ModuleExecutionBoundary? GetExecutor(string moduleName)
    {
        return _executors.TryGetValue(moduleName, out var executor) ? executor : null;
    }

    public void RecordCrash(string moduleName, Exception exception)
    {
        _crashRecords[moduleName] = exception;
        _executors.Remove(moduleName);
    }

    public bool HasCrashed(string moduleName) => _crashRecords.ContainsKey(moduleName);

    public Exception? GetCrashException(string moduleName)
    {
        return _crashRecords.TryGetValue(moduleName, out var ex) ? ex : null;
    }

    public void RemoveExecutor(string moduleName)
    {
        _executors.Remove(moduleName);
        _crashRecords.Remove(moduleName);
    }
}