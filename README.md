# ShuRuk

A PowerToys-like modular toolkit for Windows, built with WinUI 3 and .NET 10, organized as a tri-entity architecture: **ShuRuk + Runtime + Module**. / 基于 WinUI 3 与 .NET 10 的模块化工具应用，由 ShuRuk 主程序和若干独立模块组成，采用三实体架构：**主程序 + 运行时 + 模块**。

## Architecture / 架构

ShuRuk 拆分为三个逻辑实体 / ShuRuk is split into three logical entities:

| Entity | Role | Artifact | Repo |
|--------|------|----------|------|
| **ShuRuk（主程序）** | Control panel, two-level navigation, module discovery, status monitor, settings. Carries no business modules. / 主控制面板、两级导航、模块发现、状态监控、设置；不携带业务模块。 | `ShuRuk.x.y.z.msix` | main repo |
| **ShuRuk Runtime（公共运行时）** | Shared base: abstractions, WinRT contracts, common UI components, common pages. / 核心共享底座：契约、WinRT 接口、通用 UI 组件、通用页面。 | `ShuRuk.Runtime.x.y.z.msix` + NuGet `ShuRuk.Runtime.x.y.z.nupkg` | main repo |
| **ShuRuk Module（业务模块）** | Independently distributable, individually installable business packages. / 独立可分发、可单独安装卸载的业务包。 | `ModuleName.x.y.z.msix` | **independent repo** |

### Repository layout / 仓库结构

ShuRuk 与各模块均以独立 Git 仓库管理 / ShuRuk and each module are maintained as independent Git repos.

```
ShuRuk/                              # main repo (ShuRuk + Runtime + UninstallHelper)
├── .github/
│   └── workflows/
│       └── prerelease.yml           # CI 自动打包发布 / CI auto pack & pre-release
├── src/
│   ├── ShuRuk.App/                  # App → ShuRuk.exe (MSIX)
│   │   ├── Assets/                  # 应用资源 / app assets
│   │   ├── Controls/                # 自定义控件 / custom controls (ModuleNavView)
│   │   ├── Helpers/                 # 辅助类 / helpers (navigation, theme, window)
│   │   ├── Models/                  # 数据模型 / data models
│   │   ├── Services/                # 服务层 / services (ExtensionLoader, HandoverService, ModuleScanner)
│   │   ├── ViewModels/              # MVVM 视图模型 / MVVM view models
│   │   ├── Views/                   # 页面 / pages (ModuleHostPage etc.)
│   │   ├── App.xaml(.cs)            # 应用入口 / app entry
│   │   ├── MainWindow.xaml(.cs)     # 主窗口 / main window (NavigationView + Frame)
│   │   ├── Package.appxmanifest     # MSIX 包清单 / MSIX manifest
│   │   └── ShuRuk.App.csproj
│   ├── ShuRuk.Runtime/              # Runtime → dll + nupkg + msix
│   │   ├── Abstractions/            # 公共基类、工具类 / shared base & utilities
│   │   ├── Contracts/               # WinRT 导出接口 / WinRT exported interfaces (IModuleExtensionInfo)
│   │   ├── Controls/                # 通用控件 / common controls (MarqueeText, SubModuleOverviewCard)
│   │   ├── Views/                   # 通用页面 / common pages (home, settings, changelog)
│   │   └── ShuRuk.Runtime.csproj    # 打包输出 nupkg + msix
│   └── ShuRuk.UninstallHelper/      # 卸载辅助程序 / uninstall helper (ref-count + runtime cleanup)
└── README.md
```

### Dependency rules / 依赖规则

- `ShuRuk → Runtime` (ProjectReference, same repo) / 主程序引用运行时（同仓库）
- `Module → ShuRuk.Runtime` (PackageReference, NuGet) / 模块引用运行时（NuGet 包）
- `Module ✗→ ShuRuk.App` (forbidden) / 模块禁止引用主程序源码
- `ShuRuk ✗→ Module.dll` (no direct reference; load via WinRT extension) / 主程序不直接引用模块，经 WinRT 扩展加载

## Features / 功能

- Module discovery: local installed (Windows extension point) + GitHub repo (API scan, one-click install) / 模块发现：本地已安装（Windows 扩展点）+ GitHub 仓库（API 扫描、一键安装）
- Runtime management: no `PackageDependency` declared, pre-flight auto-install, registry ref-count cleanup / Runtime 管理：不声明包依赖、启动前预检自动补装、注册表引用计数自动清理
- Shared settings via registry `HKCU\Software\ShuRuk\Settings\{scope}` / 注册表共享设置
- Module monitoring with real-time CPU/memory usage / 模块监控，实时 CPU/内存使用率
- Test mode with simulated module data / 测试模式，模拟模块数据
- Multi-language support (zh-CN / en-US) / 多语言支持
- Global install registry at `HKCU\Software\ShuRuk\Runtime` / 全局安装注册表

## Runtime Management / Runtime 管理机制

- **Implicit dependency / 隐式依赖**: `Package.appxmanifest` never declares `PackageDependency` on Runtime; MSIX installs succeed without Runtime present. / 清单不声明对 Runtime 的包依赖，双击安装不检查 Runtime。
- **Pre-flight auto-install / 启动前预检自动补装**: on launch, checks Runtime MSIX version ≥ minimum; if missing/low, downloads from GitHub Release, verifies SHA256, silent-installs via `Add-AppxPackage`, then restarts. / 启动时预检 Runtime 版本，缺失/过低则从 GitHub Release 下载、校验哈希、静默安装并重启。
- **Registry ref-count / 注册表引用计数**: `HKCU\Software\ShuRuk\Runtime` tracks `RefCount` + `InstalledApps`; uninstall decrements count, removes Runtime when count reaches 0. / 注册表跟踪已安装生态应用数，卸载减一，归零则清理 Runtime。
- **UninstallHelper / 卸载辅助程序**: C# console project (trimmed single-file, ~50–150KB), embedded in ShuRuk MSIX, triggered by `windows.uninstallAction`. / C# 控制台项目（裁剪单文件），嵌入主程序 MSIX，由卸载扩展点触发。

## Build / 构建

```bash
dotnet build ShuRuk.slnx
```

### Pack Runtime / 打包运行时

Runtime 打包已由 GitHub Actions workflow（`.github/workflows/prerelease.yml`）自动处理：push 到 `dev` 分支时（含源码或 workflow 文件变更）自动构建 nupkg + MSIX 并发布为 pre-release。版本自增从 release tag 解析已发布版本，遍历历史 tag 补全缺失组件。Runtime MSIX 用 `signtool /f` 直接证书文件签名，App MSIX 用 `dotnet build` + `GenerateAppxPackageOnBuild` + `AppxBundle=Never` 生成单个 msix（仅导入 My 存储区）。/ Automated by GitHub Actions on push to dev (source or workflow changes). Version bump parses published versions from release tags, searching historical tags to fill missing components. Runtime MSIX signed via signtool /f; App MSIX via dotnet build with GenerateAppxPackageOnBuild + AppxBundle=Never (My store only).

手动本地构建（仅 dll）/ Manual local build (dll only):

```bash
dotnet build src/ShuRuk.Runtime/ShuRuk.Runtime.csproj -c Release
```

### MSIX Packaged Build / MSIX 打包构建

```bash
dotnet build src/ShuRuk.App -c Debug -r win-x64 /p:WindowsPackageType=MSIX /p:EnableMsixTooling=true
```

## Distribution / 分发

### Release artifacts / Release 产出

| Artifact | Description |
|----------|-------------|
| `ShuRuk.x.y.z.msix` | ShuRuk App (embeds UninstallHelper) / ShuRuk 主程序（内嵌卸载辅助程序） |
| `ShuRuk.Runtime.x.y.z.msix` | Runtime installer (install once, shared by all modules) / 运行时安装包（装一份，所有模块共用） |

> Release 并非每次均产出 Runtime；未产出时，应引用最近一次产出的 Runtime。 / Release does not always produce Runtime; when absent, reference the most recent Runtime release.

### Package artifacts / Package 产出

| Artifact | Description |
|----------|-------------|
| `ShuRuk.Runtime.x.y.z.nupkg` | Dev-time compile reference (GitHub Packages) / 开发期编译引用包 |
| `ShuRuk.UninstallHelper.x.y.z.nupkg` | Uninstall helper (GitHub Packages) / 卸载辅助程序 |

### Version strategy / 版本策略

- Semantic versioning (Major.Minor.Patch); major bump = breaking interface change. / 语义化版本，主版本变更为破坏性接口更新。
- NuGet source: GitHub Packages (`nuget.pkg.github.com`), dev-time reference only. / NuGet 源为 GitHub Packages，仅开发期引用。

## Install / 安装

- **ShuRuk MSIX**: install `ShuRuk` MSIX; auto-installs Runtime if missing via pre-flight check. / 装 ShuRuk MSIX；缺运行时则预检自动补装。
- **Uninstall**: `ShuRuk.UninstallHelper.exe` decrements registry ref-count; removes Runtime when count reaches 0. / 卸载：引用计数减一，归零则清理 Runtime。

## Run / 运行

```bash
winapp run src/ShuRuk.App
```

## Branch Strategy / 分支策略

- `main` - Production / 生产分支
- `dev` - Development / 开发分支
- `feature/*` - Feature branches / 功能分支
