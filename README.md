# ShuRuk

A PowerToys-like toolkit for Windows, built with WinUI 3 and .NET 10, supporting external GitHub-sourced module mounting.

## Architecture / 架构

- **ShuRuk.Host** - WinUI 3 desktop application / 宿主主程序
- **ShuRuk.Contracts** - Public interfaces and data models / 公共契约接口与数据模型
- **ShuRuk.Runtime** - Module runtime with sandbox / 模块运行时与沙箱
- **ShuRuk.Infrastructure** - Infrastructure services / 基础设施服务
- **modules/** - Built-in modules via git submodules / 内置模块（git submodule）

## Build / 构建

```bash
dotnet restore
dotnet build
```

## Run / 运行

```bash
dotnet run --project src/ShuRuk.Host
```

## Branch Strategy / 分支策略

- `main` - Production / 生产分支
- `dev` - Development / 开发分支
- `feature/*` - Feature branches / 功能分支
