# ShuRuk

A PowerToys-like toolkit for Windows, built with WinUI 3 and .NET 10, supporting external GitHub-sourced module mounting.

## Architecture / 架构

- **ShuRuk.App** - WinUI 3 desktop application (startup project, outputs ShuRuk.exe, MSIX-packaged) / 宿主主程序（启动项目，输出 ShuRuk.exe，MSIX打包）
- **ShuRuk.Contracts** - Public interfaces and data models / 公共契约接口与数据模型
- **ShuRuk.Runtime** - Module runtime with sandbox / 模块运行时与沙箱
- **ShuRuk.Infrastructure** - Infrastructure services / 基础设施服务
- **modules/** - Built-in modules via git submodules / 内置模块（git submodule）

## Build / 构建

```bash
git submodule update --init --recursive
dotnet restore
dotnet build
```

### MSIX Packaged Build / MSIX打包构建

```bash
dotnet build src/ShuRuk.App -c Debug -r win-x64 /p:WindowsPackageType=MSIX /p:EnableMsixTooling=true
```

## Run / 运行

```bash
dotnet run --project src/ShuRuk.App
```

## Branch Strategy / 分支策略

- `main` - Production / 生产分支
- `dev` - Development / 开发分支
- `feature/*` - Feature branches / 功能分支
