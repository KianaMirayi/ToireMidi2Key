using System.Diagnostics;
using System.Reflection;

namespace ToireMidi2Key;

/// <summary>
/// 以管理员身份重启的统一实现：
///   · BuildElevatedStartInfo：给界面上的"以管理员重启"按钮用
///   · TryRelaunchAsAdmin    ：给启动时按开关自动提权用（config.autoElevate）
/// </summary>
public static class Elevation
{
    /// <summary>标记"这个实例已经提权过了"，避免无限重启。</summary>
    public const string ElevatedMarker = "--elevated";

    /// <summary>
    /// 坑：调试器下 Environment.ProcessPath 是 dotnet.exe 宿主，直接拿它 runas
    /// 等于启动一个不带参数的 dotnet —— 它打印帮助就退出，表现是"进程关了但没起来"。
    /// 所以优先用同目录的 apphost（ToireMidi2Key.exe）。
    /// </summary>
    public static ProcessStartInfo BuildElevatedStartInfo(IEnumerable<string>? extraArgs = null)
    {
        string baseDir = AppContext.BaseDirectory;
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = baseDir
        };

        List<string> extra = extraArgs?.ToList() ?? new List<string>();

        string appHost = Path.Combine(baseDir, "ToireMidi2Key.exe");
        if (File.Exists(appHost))
        {
            startInfo.FileName = appHost;
            startInfo.Arguments = JoinArguments(extra);
            return startInfo;
        }

        // 没有 apphost（某些调试宿主）时，退回"宿主 + 入口 dll + 原参数"
        string host = Environment.ProcessPath ?? throw new InvalidOperationException("拿不到当前进程路径");
        var arguments = new List<string>();

        string entryDll = Assembly.GetEntryAssembly()?.Location ?? "";
        bool hostIsDotnet = Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        if (hostIsDotnet && entryDll.Length > 0) arguments.Add(entryDll);

        foreach (string arg in Environment.GetCommandLineArgs().Skip(1))
        {
            if (arg.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(arg, ElevatedMarker, StringComparison.OrdinalIgnoreCase)) continue;
            arguments.Add(arg);
        }
        arguments.AddRange(extra);

        startInfo.FileName = host;
        startInfo.Arguments = JoinArguments(arguments);
        return startInfo;
    }

    /// <summary>
    /// 当前不是管理员、且没有被调试器附加时，把自己以管理员身份重启。
    /// 返回 true 表示"已经拉起了提权实例，当前进程应当退出"。
    /// </summary>
    public static bool TryRelaunchAsAdmin(IReadOnlyList<string>? originalArgs = null)
    {
        if (CoreInfo.IsAdministrator()) return false;
        if (Debugger.IsAttached) return false;   // 调试中不提权，否则断点全废

        if (originalArgs != null &&
            originalArgs.Any(a => string.Equals(a, ElevatedMarker, StringComparison.OrdinalIgnoreCase)))
            return false;

        try
        {
            Process.Start(BuildElevatedStartInfo(new[] { ElevatedMarker }));
            return true;
        }
        catch
        {
            return false;   // 用户在 UAC 上点了"否"：继续以普通权限运行
        }
    }

    private static string JoinArguments(IEnumerable<string> values) =>
        string.Join(' ', values.Where(v => !string.IsNullOrWhiteSpace(v))
                               .Select(v => v.Contains(' ') ? $"\"{v}\"" : v));
}
