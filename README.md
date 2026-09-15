# DirSize

**A Windows folder‑size analyzer built with C# / WPF (MVVM).**
**基于 C# / WPF (MVVM) 的 Windows 目录大小分析器。**

A tree‑style explorer that shows how much storage each subfolder uses — fast, live, and non‑blocking. | 用树形界面展示每个子目录占用多少磁盘空间，快速、实时、不卡界面。

---

**Read this document in** · 阅读语言： **[English](#english) · [中文](#中文)**

---

## English

### Introduction

DirSize is a desktop tool for Windows that helps you find where disk space is being used. It scans a folder's tree structure and reports the size of every subfolder, so you can quickly spot which directories are hogging your drive.

### Features

- **Tree + list layout** — browse folders in the left tree; the right panel lists the subfolders of the currently browsed directory with their sizes.
- **Fast native scanning** — powered by the Win32 API (`FindFirstFileExW`/`FindNextFileW`), with top‑level subfolders scanned in parallel.
- **Live updates** — folder sizes appear as soon as each subtree finishes scanning; the UI stays responsive while the scan runs in the background.
- **In‑memory size cache** — directories you have already measured aren't re‑scanned when you revisit them.
- **`..` quick navigation** — the right list always starts with a `..` row to jump back to the parent folder.
- **Size sorting** — the right list is sorted by size, largest first.
- **Open in Explorer** — a button opens the selected folder (right‑list selection, then tree selection, else the current folder) in File Explorer.
- **Bilingual UI** — follows the system language automatically (Chinese for `zh` systems, English otherwise). Force a language with `--lang=zh` / `--lang=en` (or `--zh` / `--en`).

### Requirements

- Windows 10 / 11 x64
- .NET 8 SDK (only needed to build / run from source)

### Build & Run

```bash
dotnet run --project DirSize
```

Publish a self‑contained single‑file exe (no .NET runtime needed on the target machine):

```bash
dotnet publish DirSize/DirSize.csproj -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
```

### How to use

1. Pick a drive from the dropdown (folders are only listed, sizes are **not** calculated yet).
2. Click **Refresh** — the chosen folder's subtree is scanned in the background and its directory sizes stream into the list in real time.
3. Double‑click a subfolder to drill in, or double‑click `..` to go up.
4. Click **Open Folder** to reveal the current selection in File Explorer.

### Releases via GitHub Actions

A CI workflow (`build-and-release`) builds the app on every push to `main` as a check. To generate a **Release** with the win‑x64 zip attached, push a version tag:

```bash
git tag v1.0.0
git push origin v1.0.0
```

---

## 中文

### 简介

DirSize 是一款 Windows 桌面工具，用来找出磁盘空间都去哪了。它会扫描目录的树形结构并统计每个子目录的大小，让你一眼定位是哪些目录占满了磁盘。

### 功能特性

- **树 + 列表布局** —— 左侧树形浏览目录，右侧列出当前浏览目录的子目录及其占用大小。
- **原生快速扫描** —— 基于 Win32 API（`FindFirstFileExW`/`FindNextFileW`）实现，顶层子目录并行递归，速度远超逐文件遍历。
- **实时刷新** —— 每个子树扫完大小立即显示，扫描在后台进行，界面不卡顿。
- **内存缓存** —— 已统计过的目录再次浏览不再重算。
- **`..` 快速返回** —— 右侧列表首行固定为 `..`，双击返回上级目录。
- **按大小排序** —— 右侧列表默认按占用大小降序，大目录排在最上。
- **打开目录** —— 一键在资源管理器中打开当前选中的目录（优先右侧选中、其次左侧选中，默认打开当前浏览目录）。
- **双语界面** —— 跟随系统语言显示：中文系统显示中文，其余显示英文；也可传 `--lang=zh` / `--lang=en`（或 `--zh` / `--en`）强制指定。

### 环境要求

- Windows 10 / 11 x64
- .NET 8 SDK（仅开发/从源码运行时需要）

### 构建与运行

```bash
dotnet run --project DirSize
```

发布为自包含单文件 exe（目标机器无需安装 .NET 运行时）：

```bash
dotnet publish DirSize/DirSize.csproj -c Release -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

### 使用说明

1. 从下拉框选择盘符（只加载目录结构，**不会**立刻计算大小）。
2. 点击**刷新** —— 后台扫描选中目录的整棵子树，目录大小实时逐个写入右侧列表。
3. 双击子目录进入下一层，双击 `..` 返回上级。
4. 点击**打开目录**可在资源管理器中打开当前的选中项。

### 通过 GitHub Actions 自动发版

仓库内置 CI 工作流（`build-and-release`）：每次 push 到 `main` 会执行构建校验；要生成 **Release**（附带 win‑x64 安装包 zip），打一个版本标签并推送即可：

```bash
git tag v1.0.0
git push origin v1.0.0
```

---

## License

尚未指定许可证。 | License not yet specified.