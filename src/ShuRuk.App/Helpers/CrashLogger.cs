namespace ShuRuk.App.Helpers;

/// <summary>
/// Shared crash logging / 共享的崩溃日志记录。
/// Consolidates the duplicate LogCrash implementations previously in App and Program
/// 整合原先重复存在于 App 与 Program 中的 LogCrash 实现。
/// </summary>
internal static class CrashLogger
{
    public static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppConstants.AppDataFolderName,
        AppConstants.CrashLogFileName);

    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppConstants.AppDataFolderName);

    /// <summary>
    /// Detailed crash log with inner-exception chain and property dump
    /// 详细崩溃日志，包含内部异常链与属性转储。
    /// </summary>
    public static void LogCrash(string context, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            var sb = new System.Text.StringBuilder();
            sb.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{context}] ");
            var depth = 0;
            for (var cur = ex; cur is not null && depth < AppConstants.InnerExceptionDepthLimit; cur = cur.InnerException, depth++)
            {
                if (depth > 0) sb.Append(" ---> InnerException: ");
                sb.Append(cur.GetType().FullName).Append(": ").Append(cur.Message).Append('\n');
                sb.Append(cur.StackTrace).Append('\n');
                foreach (var prop in cur.GetType().GetProperties().Where(p => p.Name != "Message" && p.Name != "StackTrace" && p.Name != "InnerException" && p.Name != "Data"))
                {
                    try
                    {
                        var val = prop.GetValue(cur);
                        if (val is not null) sb.Append($"  {prop.Name} = {val}\n");
                    }
                    catch { }
                }
                if (cur.Data is not null)
                {
                    foreach (System.Collections.DictionaryEntry de in cur.Data)
                    {
                        sb.Append($"  Data[{de.Key}] = {de.Value}\n");
                    }
                }
            }
            File.AppendAllText(CrashLogPath, sb.ToString() + "\n");
        }
        catch (Exception logEx) { System.Diagnostics.Debug.WriteLine($"LogCrash write failed: {logEx}"); }

        System.Diagnostics.Debug.WriteLine($"[{context}] {ex}");
    }

    /// <summary>
    /// Lightweight crash log used during early startup before DI is ready
    /// 轻量级崩溃日志，用于 DI 就绪前的早期启动阶段。
    /// </summary>
    public static void LogCrashSimple(string context, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppDataPath);
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{context}] {ex}\n");
        }
        catch (Exception logEx) { System.Diagnostics.Debug.WriteLine($"LogCrash write failed: {logEx}"); }

        System.Diagnostics.Debug.WriteLine($"[{context}] {ex}");
    }
}