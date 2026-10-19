using System.Diagnostics;

namespace ShuRuk.Runtime.Abstractions.Services;

// Host-driven module installer: invokes a module's own installer script / 宿主驱动的模块安装器：调用模块自带安装脚本
// Modules own their installers; runtime just orchestrates the call. / 模块自带安装器，运行时仅编排调用。
public sealed class ModuleInstaller
{
    public async Task<ModuleInstallResult> InstallAsync(
        ModuleInstallRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(request.InstallerPath))
            return new ModuleInstallResult(false, $"Installer not found: {request.InstallerPath}");

        // Invoke the module's own installer EXE directly (no PowerShell) / 直接调用模块自带安装器EXE
        var psi = new ProcessStartInfo(request.InstallerPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--non-interactive");
        if (!string.IsNullOrEmpty(request.InstallDir)) { psi.ArgumentList.Add("--install-dir"); psi.ArgumentList.Add(request.InstallDir); }
        if (!string.IsNullOrEmpty(request.RuntimeSource)) { psi.ArgumentList.Add("--runtime-source"); psi.ArgumentList.Add(request.RuntimeSource); }
        if (!string.IsNullOrEmpty(request.RuntimeInstallerUri)) { psi.ArgumentList.Add("--runtime-installer-uri"); psi.ArgumentList.Add(request.RuntimeInstallerUri); }

        using var proc = Process.Start(psi);
        if (proc is null) return new ModuleInstallResult(false, "Failed to start installer process.");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return proc.ExitCode == 0
            ? new ModuleInstallResult(true, stdout)
            : new ModuleInstallResult(false, $"Exit {proc.ExitCode}: {stderr}");
    }
}

public sealed record ModuleInstallRequest(
    string InstallerPath,
    string? InstallDir = null,
    string? RuntimeSource = null,
    string? RuntimeInstallerUri = null);

public sealed record ModuleInstallResult(bool Success, string Message);