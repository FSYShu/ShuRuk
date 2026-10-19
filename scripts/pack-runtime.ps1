# Pack ShuRuk.Runtime as nupkg and msix / 打包Runtime为nupkg和msix
param(
    [string]$Configuration = "Release",
    [string]$Version = "0.2.0",
    [string]$OutputDir = "artifacts"
)

$ErrorActionPreference = "Stop"
$projectPath = "src/ShuRuk.Runtime/ShuRuk.Runtime.csproj"

Write-Output "=== Pack ShuRuk.Runtime v$Version ==="

# Build and pack nupkg for module SDK consumption / 构建并打包nupkg供模块SDK引用
Write-Output "[1/2] Packing nupkg..."
dotnet pack $projectPath -c $Configuration -p:PackageVersion=$Version -o $OutputDir
if ($LASTEXITCODE -ne 0) { throw "nupkg pack failed" }

# Build msix framework package for runtime install / 构建msix框架包供运行时安装
Write-Output "[2/2] Packing msix..."
dotnet build $projectPath -c $Configuration -p:Platform=x64 -p:WindowsPackageType=MSIX -p:PackageVersion=$Version
if ($LASTEXITCODE -ne 0) { throw "msix build failed" }

Write-Output "=== Done. Artifacts in $OutputDir/ ==="