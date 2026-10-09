using Midi2Key;

namespace Midi2Key.Cli;

/// <summary>延迟测量：只测本程序自己那一段，报告里与驱动/游戏那部分分开说明。</summary>
internal static partial class Program
{
    private static int CmdLatency(int? deviceOverride, int simulateCount)
    {
        Midi2KeyConfig config = Midi2KeyConfig.Load(ConfigPath);

        if (simulateCount > 0)
        {
            // 合成测试：全部音临时映射到 F13~F24（这些键没有任何程序会响应），
            // 既能测到真实的 SendInput 调用耗时，又不会往当前窗口里打字。
            var map = new Dictionary<string, string>();
            for (int i = 0; i < 12; i++) map[(48 + i).ToString()] = "F" + (13 + i);
            config.Map = map;
            config.MinRetriggerMs = 0;
            config.ChordSpreadMs = 0;
            config.Unmapped = Mapping.ModeIgnore;
        }

        var sender = new KeySender { Mode = config.Mode };
        sender.OnError += message => Console.WriteLine(message);
        var mapping = new Mapping(config);

        using var engine = new BridgeEngine(mapping, sender, config, logNotes: false);
        engine.Start();

        if (simulateCount > 0)
        {
            Console.WriteLine($"合成延迟测试：{simulateCount} 个音符事件（按键指向 F13~F24，无副作用）");
            RunSimulation(engine, simulateCount);
            Thread.Sleep(400);
            PrintLatencyReport(engine);
            return 0;
        }

        List<MidiDeviceInfo> devices = MidiInput.ListDevices();
        if (devices.Count == 0)
        {
            Console.WriteLine("没有 MIDI 输入设备。");
            return 1;
        }
        int index = Math.Clamp(deviceOverride ?? config.Device, 0, devices.Count - 1);

        using var midi = new MidiInput(message =>
        {
            if (message.IsNoteOn) engine.PostNoteOn(message.Data1, message.Data2, message.Timestamp);
            else if (message.IsNoteOff) engine.PostNoteOff(message.Data1, message.Timestamp);
        });
        midi.Open(index);

        Console.WriteLine($"实时延迟测量：设备 [{index}] {devices[index].Name}");
        Console.WriteLine("现在正常弹琴即可。按键会真的注入到当前焦点窗口（所以在游戏乐器界面前测最准）。");
        Console.WriteLine("每 2 秒刷新一次，Ctrl+C 结束并输出总结。\n");

        bool running = true;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            running = false;
        };

        engine.ResetStats();
        while (running)
        {
            for (int i = 0; i < 20 && running; i++) Thread.Sleep(100);
            if (running) PrintLatencyReport(engine, compact: true);
        }

        Console.WriteLine();
        PrintLatencyReport(engine);
        return 0;
    }

    private static void RunSimulation(BridgeEngine engine, int count)
    {
        var random = new Random(12345);

        // 预热：先把 JIT、线程创建、首次 SendInput 的开销跑掉，别算进统计里
        for (int i = 0; i < 60; i++)
        {
            engine.PostNoteOn(48 + (i % 12), 100);
            engine.PostNoteOff(48 + (i % 12));
        }
        Thread.Sleep(400);
        engine.ResetStats();

        const int notes = 12;
        for (int i = 0; i < count; i++)
        {
            int note = 48 + (i % notes);
            var batch = new List<int> { note };
            if (i % 7 == 0)     // 偶尔来一个三音和弦
            {
                batch.Add(48 + ((i + 4) % notes));
                batch.Add(48 + ((i + 7) % notes));
            }

            foreach (int n in batch) engine.PostNoteOn(n, 100);
            Thread.Sleep(random.Next(20, 90));
            foreach (int n in batch) engine.PostNoteOff(n);
        }
    }

    private static void PrintLatencyReport(BridgeEngine engine, bool compact = false)
    {
        LatencySummary callback = engine.CallbackStats.Summarize();
        LatencySummary queue = engine.QueueWaitStats.Summarize();
        LatencySummary send = engine.SendStats.Summarize();
        LatencySummary total = engine.EngineTotalStats.Summarize();

        lock (Console.Out)
        {
            if (compact) Console.WriteLine();
            Console.WriteLine($"── 本程序内部延迟（{DateTime.Now:HH:mm:ss}）──────────────────────");
            Console.WriteLine($"  MIDI 回调 → 入队    {callback}");
            Console.WriteLine($"  入队 → 线程拾起     {queue}");
            Console.WriteLine($"  单次按键注入        {send}");
            Console.WriteLine($"  ★ 入队 → 注入完成   {total}");
            Console.WriteLine("  ────────────────────────────────────────────────");
            Console.WriteLine("  注意：这只包含本程序。你感受到的总延迟还要加上：");
            Console.WriteLine("    · WinMM / 驱动的 MIDI 输入缓冲（设备相关，常见 1~10ms）");
            Console.WriteLine("    · 游戏的输入采样（60fps 就是平均 ~8ms、最多 ~17ms）");
            Console.WriteLine("    · 音频输出缓冲（游戏内音频设置）");
            if (!compact) Console.WriteLine();
        }
    }
}
