## ToireMidi2Key 0.1.0

把 MIDI 键盘（电钢 / 数码钢琴 / 合成器）变成电脑键盘：**弹哪个音，就等于按下哪个按键**。

专治「只认键盘、不认 MIDI」的程序 —— 比如**原神的乐器**（风物之诗琴 / 镜花之琴）。

---

### 下载哪个？

| 文件 | 说明 |
|---|---|
| **`ToireMidi2Key-0.1.0-Setup.exe`** | **推荐**。双击即装（中文向导），**自带 .NET 运行时**，不需要预装任何东西，**不需要管理员权限** |
| `ToireMidi2Key-0.1.0-portable.zip` | 便携版（GUI + CLI 诊断工具），需要机器上已有 **.NET 9 / .NET 10 桌面运行时** |

### 安装（3 步）

1. 双击 `ToireMidi2Key-0.1.0-Setup.exe`：中文向导（欢迎 → 许可协议 → **选择安装位置** → 安装）
2. **安装位置选的是「父目录」**，程序会自动装进该目录下的 `ToireMidi2Key` 子文件夹：

   | 你在向导里选的 | 实际装到 |
   |---|---|
   | `E:\` | `E:\ToireMidi2Key\` |
   | `D:\临时文件` | `D:\临时文件\ToireMidi2Key\` |
   | （默认）`%LocalAppData%\Programs` | `%LocalAppData%\Programs\ToireMidi2Key\` |

   所以**绝不会**把 `ToireMidi2Key.exe`、`README.md`、`LICENSE.txt` 散落到盘根或系统目录。
3. 装完勾选「运行 ToireMidi2Key」即可启动。

### 装完怎么用（以原神为例）

1. 打开程序，「MIDI 设备」选你的键盘，点 **套用原神乐器预设**
   （默认最低音 `48`；如果你的琴最低音是 `36`，先把「预设最低音」改成 36 再套用）
2. 点 **启动**（想确认键位是否正确，可先勾「学习模式」，弹一下琴键看它识别成什么音）
3. 进游戏打开乐器界面，直接弹
4. **如果游戏是以管理员身份运行的，本程序也必须提权**（Windows UIPI 限制）：
   点顶部「以管理员重启」，或勾选「启动时自动以管理员身份运行」

### 卸载

- 开始菜单 →「卸载 ToireMidi2Key」；或安装目录里的「卸载 ToireMidi2Key」快捷方式（即 `unins000.exe`）
- 也可以在「设置 → 应用 → 已安装的应用」里卸载
- **卸载不会删除你的 `config.json`**（映射表配置保留在安装目录里）；想彻底清掉就手动删掉整个文件夹
- 若卸载时提示权限不足，右键卸载程序 →「以管理员身份运行」

### 命令行用法（脚本 / 静默安装）

```powershell
# 静默装到指定父目录（会自动补上 ToireMidi2Key 子目录）
ToireMidi2Key-0.1.0-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR="D:\临时文件"

# 静默卸载
"%LocalAppData%\Programs\ToireMidi2Key\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

### 常用诊断（在便携版的 CLI 里）

```powershell
ToireMidi2Key.Cli.exe --list        # 认不认得到你的 MIDI 键盘
ToireMidi2Key.Cli.exe --learn       # 按键，看识别出的 MIDI 音号（排查键位不对时最有用）
ToireMidi2Key.Cli.exe --selftest    # 离线自检，应输出 PASS
ToireMidi2Key.Cli.exe --latency     # 测本程序内部延迟
```

### 说明

- **只做按键模拟**：不读游戏内存、不注入 DLL、不 hook 游戏进程。但任何第三方工具都可能被游戏判定违规（原神反作弊是内核级 mhyprot），**风险不为零，请自行判断**
- 实测内部延迟：收到音符 → 按键注入完成，**平均 0.60ms**（p95 1.32ms）；体感延迟基本来自游戏或音频设备
- 协议：**MIT**（见 [LICENSE](https://github.com/KianaMirayi/ToireMidi2Key/blob/master/LICENSE)）
- 安装包用 **Inno Setup** 构建，脚本在仓库里：`installer\ToireMidi2Key.iss`

**完整使用说明与配置表见 [README](https://github.com/KianaMirayi/ToireMidi2Key#readme)**
