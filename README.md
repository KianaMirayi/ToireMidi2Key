# ToireMidi2Key.Cli — 把 MIDI 键盘变成电脑键盘

用真钢琴键盘（MIDI 键盘）当电脑键盘用：**弹哪个音，就等于按下那个电脑按键**。

最初要解决的问题：**原神的乐器（风物之诗琴 / 镜花之琴）只认电脑键盘，不认 MIDI 输入**。
你按下 MIDI 键盘的 C3 → 程序注入电脑按键 `Z` → 游戏以为你按了 `Z` → 琴响了。

```
 MIDI 键盘 ──USB-MIDI──▶ winmm (midiInOpen) ──▶ 映射表 ──▶ SendInput ──▶ 任何程序
                          ① 收音符          ② 查表     ③ 注入真实按键
```

因为注入的是"真实键盘按键"，所以游戏、记事本、浏览器、任何程序都认——不需要它支持 MIDI。

---

## 目录结构

```
ToireMidi2Key/
├─ ToireMidi2Key.slnx
├─ src/
│  ├─ ToireMidi2Key.Core/     类库：winmm P/Invoke、SendInput、映射表、翻译引擎（零 UI 依赖）
│  ├─ ToireMidi2Key.Cli/      控制台：--list / --learn / --selftest / --probe / --demo / --run
│  └─ ToireMidi2Key.App/      Avalonia 12 + CommunityToolkit.Mvvm 图形界面
└─ docs/ui-screenshot.png
```

设计要点：**所有逻辑在 Core，CLI 和 GUI 共用同一套引擎**。CLI 是排查问题的命脉（GUI 里看不到原始 MIDI 消息，`--learn` 能看到）。

---

## 快速开始

### 1. 编译

```powershell
cd E:\Programing\FrontEndProjects\ToireMidi2Key
dotnet build ToireMidi2Key.slnx
```

### 2. 先做零风险验证（不碰游戏）

```powershell
$cli = "src\ToireMidi2Key.Cli\bin\Debug\net9.0\ToireMidi2Key.Cli.exe"

& $cli --list        # 有没有识别到你的 MIDI 键盘
& $cli --selftest    # 离线自检：映射 / 和弦 / 黑键折叠 / 同音重触发，应输出 PASS
& $cli --probe       # SendInput 注入通道是否可用
& $cli --learn       # 按琴键，看每个键发的是哪个 MIDI 音号（这一步最关键）
```

`--learn` 会打印类似：

```
音符  MIDI  48   C3 / C2(Yamaha)   力度 100   通道 1   → Z
       写进 config.json："48": "Z"
```

> **为什么一定要看这个**：MIDI 60 到底叫 C4 还是 C3，软件和硬件厂商不一致（科学音名 vs Yamaha 音名）。
> 所以程序同时打印两种写法，配置里也推荐直接写**音号数字**，永远不会有歧义。

### 3. 生成原神映射

```powershell
& $cli --preset --base-note 48    # 最低音 48 → Z 排
& $cli --print-map                # 看一眼对照表
```

原神乐器只有 **C4~B6 的白键，共 21 个音**，社区通用键位是三排：

| 音域 | 按键 |
|---|---|
| 低八度 | `Z X C V B N M` |
| 中八度 | `A S D F G H J` |
| 高八度 | `Q W E R T Y U` |

`--base-note` 是"你键盘上最低那个 C 的 MIDI 音号"。
如果 `--learn` 显示你最低的 C 是 36（而不是 48），就用 `--preset --base-note 36`。

### 4. 进游戏

```powershell
# 先切到游戏窗口，打开乐器界面，再运行（或先运行再切窗口都行）
& $cli --run
```

暂停/恢复注入：踩下 **CC66**，或 GUI 里点「暂停注入」。**Ctrl+C** 退出（退出时会自动松开所有按键）。

### 5. 图形界面

```powershell
dotnet run --project src\ToireMidi2Key.App
```

界面功能：设备下拉、启动/暂停、**学习模式**（弹一下琴键自动加一行映射）、映射表编辑、参数调节、日志。
点「以管理员重启」可一键提权（注入到以管理员运行的游戏通常需要）。

---

## 配置说明（config.json，和 exe 同目录）

| 字段 | 说明 |
|---|---|
| `device` / `deviceName` | MIDI 输入设备序号 / 按名字匹配（名字非空时优先） |
| `mode` | `scancode`（推荐，游戏/DirectInput 认这个）或 `vk` |
| `noteNaming` | 音名按 `scientific`（60=C4）还是 `yamaha`（60=C3）解释 |
| `transpose` | 整体移调半音 |
| `minRetriggerMs` | 同一个音重复按下时，两次按键的最小间隔。太快游戏会吞音（默认 30ms） |
| `chordSpreadMs` | 和弦错峰：同时到达的音依次错开几毫秒，防止游戏只吃到第一个（0 = 关闭） |
| `velocityThreshold` | 小于该力度的音符忽略（防误触） |
| `unmapped` | 未映射的音：`nearest`（就近折叠，原神推荐）或 `ignore` |
| `toggleCc` | 踩这个 CC 暂停/恢复注入（默认 66；设 -1 关闭） |
| `sustainCc` / `sustainEnabled` | 延音踏板（默认 CC64） |
| `map` | `"音号或音名" → "电脑按键"`，按键可用 `Z` / `SPACE` / `F1` / `NUMPAD0` / `UP` … |

---

## 延迟：先量化，再优化

别靠感觉猜。`--latency` 专门测量**本程序内部**这一段的耗时：

```powershell
& $cli --latency                  # 实时测量：边弹边看（建议在原神乐器界面前测）
& $cli --latency --simulate 500   # 合成测量：不开设备，音临时映射到 F13~F24（无副作用）
```

本机实测（**原神 YuanShen 正在后台运行时**测的）：

| 环节 | 平均 | p95 | 最大 |
|---|---|---|---|
| 入队 → 工作线程拾起 | 0.30ms | 0.98ms | 1.57ms |
| 单次按键注入（SendInput） | 0.30ms | 0.47ms | 0.77ms |
| **★ 入队 → 注入完成** | **0.60ms** | **1.32ms** | **1.92ms** |

优化前是 0.86ms / p95 1.63ms / 最大 3.09ms。做了四件事：

1. **自旋唤醒**：引擎不再纯阻塞等事件（`ManualResetEventSlim` + 400 次自旋），事件到达微秒级被拾起，省掉一次完整线程调度。
2. **MMCSS "Pro Audio" + `ThreadPriority.Highest`**：和 ASIO 声卡驱动同一套调度策略，CPU 被游戏占满时线程唤醒抖动更小。
3. **`timeBeginPeriod(1)`**：Windows 默认定时器精度是 15.6ms —— "延迟按下 / 同音重触发 / 和弦错峰"这些靠定时器的地方原本会被拖慢最多十几毫秒。
4. **日志不再挡在按键前面**：先注入按键、再打日志；CLI 日志改独立线程异步写；GUI 日志改成 150ms 批量刷（否则快速弹奏会把 UI 线程刷爆，进而拖慢一切）。

GUI 里也会实时显示这三个数字（状态面板最下面一行）。

### 那剩下的延迟在哪？

本程序只占 **0.6ms**。你感受到的"延迟"几乎都在下面这几段里，按可能性排序：

| 可能原因 | 典型量级 | 怎么确认 / 怎么改 |
|---|---|---|
| **蓝牙耳机 / 蓝牙音箱** | **100~300ms** | 换有线耳机或 USB 声卡试一次。这是最容易被忽略、也最致命的一项 |
| 游戏音频输出缓冲 | 20~50ms | Windows 声音设置 → 设备属性 → 高级：采样率设 24bit/48000Hz（别用 192k）；关掉"音频增强"；Focusrite 用户在 Focusrite Control 里把 Buffer Size 调到 64/128 |
| 游戏输入采样 | 平均 8ms、最多 17ms（60fps） | 帧率越低采样越粗（30fps 就是 16~33ms）；关垂直同步、用独占全屏、提高帧率上限 |
| WinMM MIDI 输入缓冲 | 1~10ms（设备相关） | 没法直接测（`--latency` 只能测到"回调之后"）。要真测需要把键盘 MIDI OUT 环回到声卡 MIDI IN 做环回测量 |

**一分钟判断法**：分别在原神里弹、和用 `--demo` 往记事本里"打字"。
- 记事本里字符是即时出现的 → 本程序没问题，延迟在游戏的输入/音频那一段。
- 记事本里也明显慢半拍 → 那是 MIDI 输入或注入被拦了，把 `--latency` 的数字发我。

## 已知限制（重要）

1. **原神乐器没有黑键**。它只有 21 个白键，所以任何含黑键的曲子必然要"就近折叠"或跳过。
   想要完全还原，只能靠移调（`transpose`）把曲子挪到大调。
2. **快速同音重复会被限制**。游戏在 `keyup` 到下一个 `keydown` 之间需要间隔，我们已用
   `minRetriggerMs` 处理；但极快的重复（<30ms）本质上无法演奏。
3. **力度/触后感丢失**。电脑键盘只有"按下/松开"两种状态，MIDI 力度信息没有地方表达。
4. **需要管理员权限**才能注入到以管理员运行的程序（原神就是）。
5. **第三方工具风险**：任何形式的第三方脚本/工具都有被游戏官方判定违规的可能
   （原神的反作弊是内核级的 mhyprot）。本工具**只做按键模拟**：不读游戏内存、不注入 DLL、
   不 hook 游戏进程，这是风险最低的做法，但**风险不为零，请自行判断**。

## 故障排查

| 现象 | 原因 / 处理 |
|---|---|
| `--list` 找不到设备 | 换 USB 口/线；键盘是否在 MIDI 模式；关掉独占它的 DAW |
| 游戏里按了没反应 | 用管理员运行；把 `mode` 改成 `vk` 再试；确认乐器界面已打开 |
| 第一个音对，整体高/低八度 | 重设 `--base-note`，或用 `transpose` |
| 黑键弹出来音不对 | 正常，原神没有黑键；把曲子移调成不含黑键的调 |
| 快速乐句掉音 | 调大 `minRetriggerMs`（40~60），或调 `chordSpreadMs` |
| 按键卡住不松 | 退出程序会自动松开；也可以点 GUI 的「紧急松开」 |

## 回滚

删掉项目目录 + 删掉 exe 同目录的 `config.json` 即可，不改注册表、不装驱动、不写系统目录。
