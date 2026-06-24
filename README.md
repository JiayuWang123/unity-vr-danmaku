# Unity VR 弹幕项目

基于 Unity 2022.3 的 VR 项目，支持视频播放与 ASS 字幕弹幕解析展示。

## 环境要求

| 项目 | 要求 |
|------|------|
| Unity | **2022.3.48f1c1**（必须与此版本一致） |
| Git | 用于克隆与版本协作 |
| 磁盘空间 | 建议至少 8 GB（项目资源约 3 GB + Unity 缓存） |

## 快速开始

### 1. 克隆仓库

```bash
git clone https://github.com/JiayuWang123/unity-vr-danmaku.git
cd unity-vr-danmaku
```

### 2. 用 Unity Hub 打开

1. 打开 [Unity Hub](https://unity.com/download)
2. 安装 **Unity 2022.3.48f1**（若尚未安装）
3. 点击 **Open** → 选择克隆下来的项目文件夹
4. 首次打开会生成 `Library/` 并自动安装依赖包，可能需要 **10–30 分钟**，请耐心等待

### 3. 运行项目

1. 在 Project 窗口打开 `Assets/Scenes/testScene.unity`
2. 点击 Play 运行
3. 测试弹幕文件位于 `Assets/StreamingAssets/test.ass`

## 依赖说明

项目依赖已配置在 `Packages/manifest.json` 中，**无需手动安装**。Unity 首次打开时会自动下载，主要包括：

- XR Interaction Toolkit / OpenXR / XR Management（VR 支持）
- TextMeshPro（UI 文字）
- UOS Launcher（Unity 在线服务，按需配置）

## VR 测试

- **编辑器模拟**：可使用 XR Device Simulator 在编辑器内测试，无需头显
- **真机测试**：需要 OpenXR 兼容头显及对应运行时（如 SteamVR、Meta Quest Link 等）

## UOS 服务（可选）

若项目使用 UOS 云服务：

1. 菜单栏选择 **UOS → Open Launcher**
2. 在 [UOS 官网](https://uos.unity.cn/apps) 获取 AppID 等信息并填入
3. **切勿将 AppSecret 等密钥提交到 Git**

## 团队协作

```bash
git pull                              # 开始工作前先拉取最新代码
git checkout -b feature/your-feature  # 创建功能分支
# ... 修改并测试 ...
git add .
git commit -m "描述你的修改"
git push -u origin feature/your-feature
```

建议在 GitHub 上通过 Pull Request 合并到 `main` 分支。

## 注意事项

- `Library/`、`Temp/`、`Logs/` 等目录由 Unity 自动生成，**不要提交到 Git**
- 所有成员请使用相同 Unity 版本，避免 `ProjectSettings` 冲突
- 首次 `git clone` 较慢属正常现象（项目资源较大）

## 项目结构

```
Assets/
  Codes/           # 弹幕解析与 ASS 管理脚本
  Scenes/          # 场景文件（主场景：testScene.unity）
  StreamingAssets/ # 运行时读取的外部文件（ASS/XML 等）
  XR/              # XR / OpenXR 配置
Packages/          # Unity 包依赖清单
ProjectSettings/   # 项目设置
```

## Danmaku Burst Map Analysis Tool

This repository also includes an XML-only danmaku burst-map analysis tool:

```text
tools/danmaku_burst_map/
```

It accepts a Bilibili danmaku XML file and generates density charts, burst event
tables, topic/emotion/content evidence, and a Markdown analysis report. A
pre-filtered JSON file is not required.

Python usage:

```bash
python tools/danmaku_burst_map/python/run_burst_map.py ^
  --input path/to/danmaku.xml ^
  --output outputs/danmaku_burst_map_generalized
```

MATLAB usage:

```matlab
run_burst_map("path/to/danmaku.xml", "outputs/danmaku_burst_map_generalized", "tools/danmaku_burst_map/configs/default.yaml")
```

See `tools/danmaku_burst_map/docs/burst_map_usage.md` for details.
