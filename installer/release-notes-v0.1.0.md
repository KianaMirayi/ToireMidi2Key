## ToireMidi2Key 0.1.0

把 MIDI 键盘（电钢 / 数码钢琴 / 合成器）变成电脑键盘：**弹哪个音，就等于按下哪个按键**。

专治「只认键盘、不认 MIDI」的程序 —— 比如**原神的乐器**（风物之诗琴 / 镜花之琴）。

---

### 下载哪个？

| 文件 | 说明 |
|---|---|
| **`ToireMidi2Key-0.1.0-Setup.msi`** | **推荐**。双击即装，**自带 .NET 运行时**，不需要预装任何东西；装到用户目录，**不弹 UAC** |
| `ToireMidi2Key-0.1.0-portable.zip` | 便携版（GUI + CLI 诊断工具），需要机器上已有 **.NET 9 / .NET 10 桌面运行时** |

### 安装包里有什么

- **中文安装向导**：欢迎页 → 许可协议 → **可自行选择安装位置**（**支持中文路径**，例如 `D:\临时文件\ToireMidi2Key`）
- **默认安装位置**：`%LocalAppData%\Programs\ToireMidi2Key`（用户目录，无需管理员权限）
- **快捷方式**：开始菜单 + 桌面
- **程序配置 `config.json` 就在安装目录里、和 exe 同目录**（不会写到 AppData 或别的盘）
- **卸载**：设置 → 应用 → 已安装的应用；卸载**不会删除你的 `config.json`**，重装后映射表还在
- 自带 .NET 运行时，**不需要预装任何东西**

### 装完怎么用（以原神为例）

1. 打开程序，「MIDI 设备」选你的键盘，点 **套用原神乐器预设**
   （默认最低音 `48`；如果你的琴最低音是 `36`，先把「预设最低音」改成 36 再套用）
2. 点 **启动**（想确认键位是否正确，可先勾「学习模式」，弹一下琴键看它识别成什么音）
3. 进游戏打开乐器界面，直接弹
4. **如果游戏是以管理员身份运行的，本程序也必须提权**（Windows UIPI 限制）：
   点顶部「以管理员重启」，或勾选「启动时自动以管理员身份运行」

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

**完整使用说明与配置表见 [README](https://github.com/KianaMirayi/ToireMidi2Key#readme)**
