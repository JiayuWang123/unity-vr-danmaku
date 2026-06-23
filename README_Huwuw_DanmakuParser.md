# Huwuw XML 弹幕解析与 Unity 挂载说明

本分支基于 `Huwuw/danmaku-parser-script`，在原 Unity VR 弹幕项目中加入了 XML 弹幕筛选、JSON 统一格式和运行时播放挂载流程。

## 本次更改

- 新增 Bilibili XML 弹幕解析与筛选脚本。
- 新增统一 JSON 弹幕文件：`Assets/StreamingAssets/filtered_danmaku.json`。
- 将 `Assets/Scenes/testScene.unity` 的弹幕播放入口从旧 `ASSManager` 切换为 `DanmakuPlaybackController`。
- 复用原场景里的 `VideoPlayer`、`DanmakuCanvas2` 和 `DanmakuTemplate`，不重建 UI。
- 保留旧 `ASSManager.cs` 和 `DanmakuParser.cs` 源文件作为回退，但场景默认不再使用它们。
- 添加 `Assets/Codes/ReferencesUsed.md`，记录筛选和解析规则参考的论文。

## 运行方式

1. 用 Unity 2022.3 打开项目。
2. 打开 `Assets/Scenes/testScene.unity`。
3. 点击 Play。
4. 视频播放后，`SystemManager` 上的 `DanmakuPlaybackController` 会读取 `Assets/StreamingAssets/filtered_danmaku.json`。
5. 弹幕实例会从现有 `DanmakuTemplate` 生成，挂到 `DanmakuCanvas2` 下，并按视频时间从右向左移动。

## 当前场景挂载逻辑

`testScene.unity` 中的关键对象如下：

| 对象 | 当前用途 |
|------|----------|
| `SystemManager` | 挂载 `DanmakuPlaybackController`，负责读取 JSON 和按视频时间生成弹幕 |
| `DanmakuCanvas2` | World Space Canvas，作为弹幕父节点 |
| `DanmakuTemplate` | TextMeshProUGUI 模板，运行时会被复制生成弹幕 |
| `VideoPlayer` | 提供当前视频时间，控制弹幕同步 |
| `DanmakuManager` | 旧 `DanmakuParser` 已禁用，避免重复解析和日志干扰 |

## 脚本说明

### `DanmakuEntry.cs`

定义统一弹幕数据结构和统计结构。运行时 JSON 会反序列化为 `DanmakuCollection`，其中包含 `entries` 和 `stats`。

### `DanmakuXmlNormalizer.cs`

负责从 XML 中读取 Bilibili `<d p="...">text</d>` 弹幕，进行清洗、筛选、限流和轨道分配，然后导出 JSON。

筛选规则包括：

- 跳过空文本、非法时间戳、字段不足和无法解析的弹幕。
- 过滤单字、纯符号、短数字和重复单字符内容。
- 保留有体育语境价值的短反应，例如 `牛逼`、`进了`、`好球`、`goal`。
- 对短时间内重复内容降权，减少刷屏。
- 按时间窗口限制密度，避免高峰片段一次性生成过多弹幕。

### `DanmakuPlaybackController.cs`

运行时播放控制器，挂在 `SystemManager` 上。

主要逻辑：

- 从 `StreamingAssets` 读取 `filtered_danmaku.json`。
- 按 `VideoPlayer.time` 触发弹幕生成。
- 使用 JSON 中的 `trackIndex` 把弹幕分配到不同高度。
- 支持视频回退或重播时重置弹幕索引。
- 启动时隐藏 `DanmakuTemplate`，但保留它作为实例化模板。

### `DanmakuMover.cs`

负责弹幕实例向左移动，并在离开 Canvas 左侧后销毁对象。

## JSON 字段

`filtered_danmaku.json` 的顶层结构：

```json
{
  "formatVersion": "1.0",
  "generatedAtUtc": "2026-06-23T...",
  "entries": [],
  "stats": {}
}
```

每条 `entries` 弹幕包含：

| 字段 | 说明 |
|------|------|
| `timeSeconds` | 弹幕对应的视频时间，单位为秒 |
| `text` | 清洗后的弹幕文本 |
| `mode` | Bilibili 原始弹幕模式 |
| `modeName` | 模式名称，如 `scroll`、`top`、`bottom` |
| `fontSize` | 原始字号 |
| `colorHex` | 十六进制颜色 |
| `userHash` | 匿名用户哈希 |
| `danmakuId` | 弹幕 ID |
| `sourceFile` | 来源 XML 文件名 |
| `trackIndex` | 预分配的显示轨道 |
| `priorityScore` | 筛选和限流时使用的优先级分数 |

`stats` 中记录来源文件数量、解析数量、保留数量、时间范围、模式分布和过滤原因统计。

## 参考文献使用

筛选和解析规则参考了 `Reference` 文件夹中的弹幕相关论文，具体记录见：

```text
Assets/Codes/ReferencesUsed.md
```

主要参考方向包括：

- 时间同步弹幕的可读性和过载控制。
- Bilibili 弹幕重复表达与群体语义。
- VR/沉浸式观看中的异步共同观看体验。
- 保留短情绪反应作为体育赛事关键时刻信号。

## 后续调整建议

- 如需重新生成 JSON，可在 Unity 中给空对象临时挂载 `DanmakuXmlNormalizer`，设置 XML 输入目录后执行 Context Menu：`Normalize XML To JSON`。
- 如需更少弹幕，可降低 `DanmakuXmlNormalizer.maxEntriesPerWindow` 后重新导出 JSON。
- 如需更稀疏的屏幕布局，可调大 `DanmakuPlaybackController.trackCount` 或调整 Canvas 高度与字体大小。
