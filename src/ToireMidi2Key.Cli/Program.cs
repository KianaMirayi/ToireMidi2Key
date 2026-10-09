using System.Diagnostics;
using System.Text;
using ToireMidi2Key;

namespace ToireMidi2Key.Cli;

/// <summary>
/// 命令行版：既能当正式工具用，也是排查问题的命脉。
/// 引擎、映射、注入全在 ToireMidi2Key.Core 里，和 Avalonia 界面共用同一份逻辑。
/// </summary>
internal static partial class Program
{
    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "config.json");

    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* 某些终端不支持 */ }

        // 兜底：任何退出路径都要把按下的键松开，不然系统会留一个"卡住的键"
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SafeReleaseAll();

        try
        {
            return Dispatch(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"出错：{ex.Message}");
            return 2;
        }
    }

    private static readonly List<KeySender> ActiveSenders = new();

    private static void SafeReleaseAll()
    {
        foreach (KeySender sender in ActiveSenders)
        {
            try { sender.ReleaseAll(); } catch { /* 忽略 */ }
        }
    }

    private static int Dispatch(string[] args)
    {
        string command = "run";
        int? device = null;
        string mode = null;
        int baseNote = GenshinPreset.DefaultBaseNote;
        bool dryRun = false;
        bool quiet = false;
        int simulate = 0;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--list" or "list": command = "list"; break;
                case "--learn" or "learn": command = "learn"; break;
                case "--selftest" or "selftest": command = "selftest"; break;
                case "--probe" or "probe": command = "probe"; break;
                case "--demo" or "demo": command = "demo"; break;
                case "--latency" or "latency": command = "latency"; break;
                case "--preset" or "preset": command = "preset"; break;
                case "genshin": command = "preset"; break;
                case "--print-map" or "print-map" or "map": command = "print-map"; break;
                case "--run" or "run": command = "run"; break;
                case "--help" or "-h" or "help": command = "help"; break;
                case "--device": device = int.Parse(args[++i]); break;
                case "--mode": mode = args[++i]; break;
                case "--base-note": baseNote = int.Parse(args[++i]); break;
                case "--simulate": simulate = int.Parse(args[++i]); break;
                case "--dry-run": dryRun = true; break;
                case "-q" or "--quiet": quiet = true; break;
                case "-v" or "--verbose": quiet = false; break;
                default:
                    Console.Error.WriteLine($"忽略不认识的参数：{arg}");
                    break;
            }
        }

        return command switch
        {
            "list" => CmdList(),
            "learn" => CmdLearn(device),
            "selftest" => CmdSelfTest(),
            "probe" => CmdProbe(),
            "demo" => CmdDemo(device, mode),
            "latency" => CmdLatency(device, simulate),
            "preset" => CmdPreset(baseNote),
            "print-map" => CmdPrintMap(),
            "help" => CmdHelp(),
            _ => CmdRun(device, mode, dryRun, quiet)
        };
    }

    private static int CmdHelp()
    {
        Console.WriteLine("""
            ToireMidi2Key.Cli — 把 MIDI 键盘当成电脑键盘用

              ToireMidi2Key.Cli                      正式运行（读同目录 config.json）
              ToireMidi2Key.Cli --list               列出 MIDI 输入设备
              ToireMidi2Key.Cli --learn              学习模式：按琴键，只打印音名/音号，不注入按键
              ToireMidi2Key.Cli --selftest           离线自检：不开设备、不注入，验证映射与按键逻辑
              ToireMidi2Key.Cli --probe              探测 SendInput 通道是否可用（发无害的 F24）
              ToireMidi2Key.Cli --demo               真实注入演示：切到记事本，看它"打字"
              ToireMidi2Key.Cli --preset [--base-note 48]   生成原神乐器三排映射，写回 config.json
              ToireMidi2Key.Cli --print-map          打印当前映射对照表

            选项：--device N  --mode vk|scancode  --dry-run（只打印不注入）  -q
            """);
        return 0;
    }

    private static int CmdList()
    {
        List<MidiDeviceInfo> devices = MidiInput.ListDevices();
        Console.WriteLine($"MIDI 输入设备：{devices.Count} 个");
        foreach (MidiDeviceInfo device in devices)
            Console.WriteLine($"  [{device.Index}] {device.Name}");

        if (devices.Count == 0)
        {
            Console.WriteLine("  没找到设备。检查：USB 线 / 键盘是否在 MIDI 模式 / 驱动是否装好；");
            Console.WriteLine("  另外 WinMM 只认系统级 MIDI 输入端口，被 DAW 独占时会打不开。");
        }
        return 0;
    }

    private static int CmdPrintMap()
    {
        ToireMidi2KeyConfig config = ToireMidi2KeyConfig.Load(ConfigPath);
        var mapping = new Mapping(config);

        Console.WriteLine($"配置：{ConfigPath}");
        Console.WriteLine($"注入方式 {config.Mode}    未映射策略 {config.Unmapped}    移调 {config.Transpose}");
        Console.WriteLine();
        Console.WriteLine("  MIDI  科学音名  Yamaha  按键   虚拟键码  扫描码");
        Console.WriteLine("  ----  --------  ------  -----  --------  ------");
        foreach (int note in mapping.Notes)
        {
            if (!mapping.TryResolve(note, Mapping.ModeIgnore, out string key, out ushort vk, out _)) continue;
            Console.WriteLine($"  {note,4}  {NoteName.Scientific(note),-8}  {NoteName.Yamaha(note),-6}  {key,-5}  0x{vk:X2}      0x{KeySender.ScanCode(vk):X2}");
        }
        Console.WriteLine($"  共 {mapping.Count} 个映射");

        foreach (string warning in mapping.Warnings) Console.WriteLine($"  [警告] {warning}");
        return 0;
    }

    private static int CmdPreset(int baseNote)
    {
        ToireMidi2KeyConfig config = ToireMidi2KeyConfig.Load(ConfigPath);
        config.Map = GenshinPreset.Build(baseNote);
        config.Save(ConfigPath);

        Console.WriteLine($"已写入原神乐器预设（最低音 MIDI {baseNote} = {NoteName.Describe(baseNote)}）：");
        Console.WriteLine();
        Console.WriteLine("  Z 排 = 低八度，A 排 = 中八度，Q 排 = 高八度");
        foreach (KeyValuePair<string, string> entry in config.Map.OrderBy(e => int.Parse(e.Key)))
        {
            int note = int.Parse(entry.Key);
            Console.WriteLine($"  MIDI {note,3}  {NoteName.Describe(note),-22} → {entry.Value}");
        }
        Console.WriteLine();
        Console.WriteLine($"配置文件：{ConfigPath}");
        Console.WriteLine("提示：先用 --learn 确认你键盘最低那个 C 是几号音，如果和上面不符，用 --base-note <音号> 重新生成。");
        return 0;
    }

    private static int CmdLearn(int? deviceOverride)
    {
        List<MidiDeviceInfo> devices = MidiInput.ListDevices();
        if (devices.Count == 0)
        {
            Console.WriteLine("没有 MIDI 输入设备。");
            return 1;
        }

        ToireMidi2KeyConfig config = ToireMidi2KeyConfig.Load(ConfigPath);
        int index = Math.Clamp(deviceOverride ?? config.Device, 0, devices.Count - 1);
        var mapping = new Mapping(config);

        using var midi = new MidiInput(message =>
        {
            if (message.IsNoteOn)
            {
                string target = mapping.TryResolve(message.Data1, config.Unmapped, out string key, out _, out int mappedNote)
                    ? (mappedNote == message.Data1 ? key : $"{key}（就近折叠自 {NoteName.Scientific(mappedNote)}）")
                    : "未映射";
                Console.WriteLine($"音符  MIDI {message.Data1,3}   {NoteName.Scientific(message.Data1),-4} / {NoteName.Yamaha(message.Data1),-4}(Yamaha)   力度 {message.Data2,3}   通道 {message.Channel + 1}   → {target}");
                Console.WriteLine($"       写进 config.json：\"{message.Data1}\": \"Z\"");
            }
            else if (message.IsControlChange)
            {
                Console.WriteLine($"CC    {message.Data1,3} = {message.Data2,3}   通道 {message.Channel + 1}");
            }
        });

        midi.Open(index);
        Console.WriteLine($"学习模式已开启：设备 [{index}] {devices[index].Name}");
        Console.WriteLine("按琴键看音号/音名；不会注入任何按键。Ctrl+C 退出。");
        WaitForExit(TimeSpan.FromDays(1));
        return 0;
    }

    private static int CmdProbe()
    {
        int size = KeySender.InputStructSize;
        Console.WriteLine($"INPUT 结构体大小：{size} 字节（进程 {(CoreInfo.Is64BitProcess ? "x64，应为 40" : "x86，应为 28")}）");
        Console.WriteLine($"当前权限：{(CoreInfo.IsAdministrator() ? "管理员" : "普通用户")}");

        var sender = new KeySender { Mode = KeySender.ModeScancode };
        sender.OnError += message => Console.WriteLine(message);
        ushort f24 = Keys.Resolve("F24");       // F24 几乎没有任何程序会响应，用来探测最安全
        sender.Down("F24", f24);
        sender.Up("F24");

        if (sender.Failures == 0)
        {
            Console.WriteLine("SendInput 通道正常：按键已成功注入（F24 无副作用）。");
            return 0;
        }
        Console.WriteLine($"SendInput 注入失败 {sender.Failures} 次。");
        return 1;
    }

    private static int CmdSelfTest()
    {
        Console.WriteLine("=== 离线自检（不打开设备、不注入真实按键）===");
        ToireMidi2KeyConfig config = ToireMidi2KeyConfig.CreateDefault();
        var sender = new KeySender { Mode = KeySender.ModeScancode, DryRun = true };
        var mapping = new Mapping(config);

        Console.WriteLine($"默认映射：{mapping.Count} 个音（原神乐器应为 21 个）");
        if (mapping.Count != GenshinPreset.Rows.Sum(r => r.Length))
            Console.WriteLine("  [警告] 映射数量不是 21");

        using var engine = new BridgeEngine(mapping, sender, config, logNotes: true);
        engine.OnLog += message => Console.WriteLine($"      {message}");
        engine.Start();

        Console.WriteLine("\n[1] 三和弦：MIDI 48 / 52 / 55 → 应按下 Z C B，再全部松开");
        engine.PostNoteOn(48, 100);
        engine.PostNoteOn(52, 100);
        engine.PostNoteOn(55, 100);
        Thread.Sleep(150);
        engine.PostNoteOff(48);
        engine.PostNoteOff(52);
        engine.PostNoteOff(55);
        Thread.Sleep(150);

        Console.WriteLine("\n[2] 黑键折叠：MIDI 49（C#3）不在映射里 → 应就近折到 48 → Z");
        engine.PostNoteOn(49, 100);
        Thread.Sleep(80);
        engine.PostNoteOff(49);
        Thread.Sleep(150);

        Console.WriteLine("\n[3] 超范围折叠：MIDI 96 高于最高映射音 83 → 应折到 83 → U");
        engine.PostNoteOn(96, 100);
        Thread.Sleep(80);
        engine.PostNoteOff(96);
        Thread.Sleep(150);

        Console.WriteLine("\n[4] 同音重触发：连按两次 48 → 应先松开再重新按下（Z↓ Z↑ Z↓ Z↑）");
        engine.PostNoteOn(48, 100);
        engine.PostNoteOn(48, 100);
        Thread.Sleep(120);
        engine.PostNoteOff(48);
        engine.PostNoteOff(48);
        Thread.Sleep(250);

        string[] expected =
        {
            "Z↓", "C↓", "B↓", "Z↑", "C↑", "B↑",
            "Z↓", "Z↑",
            "U↓", "U↑",
            "Z↓", "Z↑", "Z↓", "Z↑"
        };
        string[] actual = sender.Log.Select(e => e.Key + (e.Down ? "↓" : "↑")).ToArray();

        Console.WriteLine("\n期望：" + string.Join(" ", expected));
        Console.WriteLine("实际：" + string.Join(" ", actual));
        Console.WriteLine($"结尾是否有键没松开：{(sender.Log.Count > 0 && sender.Log.Last().Down ? "是（有卡键！）" : "否")}");

        bool pass = actual.SequenceEqual(expected);
        Console.WriteLine(pass ? "\n结果：PASS" : "\n结果：FAIL");
        return pass ? 0 : 1;
    }

    private static int CmdDemo(int? deviceOverride, string modeOverride)
    {
        ToireMidi2KeyConfig config = ToireMidi2KeyConfig.Load(ConfigPath);
        if (!string.IsNullOrEmpty(modeOverride)) config.Mode = modeOverride;

        var sender = new KeySender { Mode = config.Mode };
        sender.OnError += message => Console.WriteLine(message);
        lock (ActiveSenders) ActiveSenders.Add(sender);

        var mapping = new Mapping(config);
        using var engine = new BridgeEngine(mapping, sender, config, logNotes: true);
        engine.OnLog += message => Console.WriteLine("   " + message);
        engine.Start();

        Console.WriteLine($"注入方式：{config.Mode}    映射：{mapping.Count} 个音");
        Console.WriteLine("3 秒后开始注入按键 → 请立刻点一下记事本/文本编辑器，让光标在那里。");
        Console.WriteLine("（注意：按键会进入当前有焦点的窗口，别让它落在这个终端里）");
        for (int i = 3; i >= 1; i--)
        {
            Console.WriteLine($"  {i} …");
            Thread.Sleep(1000);
        }

        int[] scale = mapping.Notes.Take(8).ToArray();
        Console.WriteLine("→ 上行音阶");
        foreach (int note in scale)
        {
            engine.PostNoteOn(note, 100);
            Thread.Sleep(90);
            engine.PostNoteOff(note);
            Thread.Sleep(70);
        }

        Console.WriteLine("→ 一个和弦");
        foreach (int note in scale.Take(3)) engine.PostNoteOn(note, 100);
        Thread.Sleep(500);
        foreach (int note in scale.Take(3)) engine.PostNoteOff(note);
        Thread.Sleep(200);

        engine.Panic();
        Thread.Sleep(150);
        Console.WriteLine("演示结束。没看到字符 → 用 --mode vk 再试一次，或改用管理员终端运行。");
        return 0;
    }

    private static int CmdRun(int? deviceOverride, string modeOverride, bool dryRun, bool quiet)
    {
        ToireMidi2KeyConfig config = ToireMidi2KeyConfig.Load(ConfigPath);
        if (deviceOverride.HasValue) config.Device = deviceOverride.Value;
        if (!string.IsNullOrEmpty(modeOverride)) config.Mode = modeOverride;

        List<MidiDeviceInfo> devices = MidiInput.ListDevices();
        if (devices.Count == 0)
        {
            Console.WriteLine("没有 MIDI 输入设备，先接好键盘再看 --list。");
            return 1;
        }
        int index = Math.Clamp(config.Device, 0, devices.Count - 1);

        var mapping = new Mapping(config);
        var sender = new KeySender { Mode = config.Mode, DryRun = dryRun };
        sender.OnError += message => Console.WriteLine(message);
        lock (ActiveSenders) ActiveSenders.Add(sender);

        using var engine = new BridgeEngine(mapping, sender, config, logNotes: !quiet);
        using var consoleLog = new AsyncConsoleLog();
        engine.OnLog += consoleLog.Write;      // 日志走独立线程，绝不拖慢按键注入
        engine.OnStateChanged += () => consoleLog.Write(engine.Enabled ? "  [状态] 已启用" : "  [状态] 已暂停");

        using var midi = new MidiInput(message =>
        {
            if (message.IsNoteOn) engine.PostNoteOn(message.Data1, message.Data2);
            else if (message.IsNoteOff) engine.PostNoteOff(message.Data1);
            else if (message.IsControlChange) engine.PostControlChange(message.Data1, message.Data2);
        });

        engine.Start();
        midi.Open(index);

        Console.WriteLine($"设备：[{index}] {devices[index].Name}");
        Console.WriteLine($"注入方式：{config.Mode}{(dryRun ? "（dry-run：只打印不注入）" : "")}    映射：{mapping.Count} 个音");
        Console.WriteLine($"未映射策略：{config.Unmapped}    移调：{config.Transpose}    切换开关 CC：{config.ToggleCc}");
        Console.WriteLine($"权限：{(CoreInfo.IsAdministrator() ? "管理员" : "普通用户（注入到管理员游戏可能无效）")}");
        foreach (string warning in mapping.Warnings) Console.WriteLine($"[警告] {warning}");
        Console.WriteLine("正在翻译… Ctrl+C 退出（退出时会松开所有按键）");

        WaitForExit(TimeSpan.FromDays(1));

        engine.Panic();
        Console.WriteLine("已退出。");
        return 0;
    }

    private static void WaitForExit(TimeSpan max)
    {
        using var done = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;      // 交给我们的清理逻辑，而不是直接杀进程
            done.Set();
        };
        done.Wait(max);
    }
}
