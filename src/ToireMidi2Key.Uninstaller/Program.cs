// ToireMidi2Key 卸载程序
//
// 放在安装目录里（安装时一起装进去），双击即可卸载。
// 做法：在注册表的「卸载」项里找已安装的 ToireMidi2Key，拿到它的 ProductCode，再调用 msiexec /x。
// 为什么不写死 ProductCode：ProductCode 是每次打包时生成的，写死的话重新打包后这个 exe 就失效了。
// 用「注册表查找」则任何版本都能正确卸载。
//
// 权限：本程序是每用户安装的，一般不需要管理员就能卸载；
//       但如果卸载失败或提示权限不足（例如从「设置 → 应用」卸载、或程序被以管理员身份装过），
//       就需要以管理员身份运行本程序 —— 运行时会打印这条提示。
//
// 参数：
//   --yes / -y    跳过确认（脚本里用）
//   --quiet / -q  结束后不等待按键

using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace ToireMidi2Key.Uninstaller;

internal static class Program
{
    private const string ProductName = "ToireMidi2Key";

    private static int Main(string[] args)
    {
        var autoYes = args.Any(a => a is "--yes" or "-y");
        var quiet = args.Any(a => a is "--quiet" or "-q");

        // 本程序就在安装目录里，而卸载时 MSI 需要删除它自己。
        // 直接从这里调用 msiexec，Windows Installer 会弹出
        // 「下列应用程序应该关闭才能继续安装：uninstall」并卡住。
        // 所以先把自己复制到 %TEMP% 再从那里重启，本进程立刻退出（复制件不在安装目录，不占用被删文件）。
        try
        {
            var self = Environment.ProcessPath;
            var tempExe = Path.Combine(Path.GetTempPath(), "ToireMidi2Key-uninstall", "uninstall.exe");
            if (self is not null && !string.Equals(self, tempExe, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(tempExe)!);
                File.Copy(self, tempExe, overwrite: true);
                var relaunch = new ProcessStartInfo(tempExe) { UseShellExecute = false };
                foreach (var a in args) relaunch.ArgumentList.Add(a);
                Process.Start(relaunch);
                return 0;
            }
        }
        catch
        {
            // 复制失败就原地继续，顶多 MSI 再弹一次「正在使用中」
        }

        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* 控制台编码失败不影响卸载 */ }
        try { Console.Title = "卸载 ToireMidi2Key"; } catch { }

        var isAdmin = IsElevated();

        Console.WriteLine();
        Console.WriteLine("  ToireMidi2Key 卸载程序");
        Console.WriteLine("  ────────────────────────────────");
        Console.WriteLine("  当前权限：" + (isAdmin ? "管理员 ✓" : "普通用户"));
        Console.WriteLine("  👉 提示：建议【以管理员身份运行】本程序来卸载；");
        Console.WriteLine("     如果卸载失败或提示权限不足，请右键本程序 →「以管理员身份运行」后重试。");
        Console.WriteLine();

        var found = FindProducts();
        if (found.Count == 0)
        {
            Console.WriteLine("  没有找到已安装的 ToireMidi2Key（可能已经卸载过了）。");
            Pause(quiet);
            return 1;
        }

        foreach (var item in found)
        {
            Console.WriteLine("  安装位置：" + (string.IsNullOrWhiteSpace(item.Location) ? "（未记录）" : item.Location));
            Console.WriteLine("  产品代码：" + item.Code);
        }

        if (!autoYes)
        {
            Console.WriteLine();
            Console.Write("  确认卸载吗？(Y = 卸载，其他键 = 取消) ");
            var confirmed = false;
            try { confirmed = Console.ReadKey(intercept: true).Key == ConsoleKey.Y; } catch { confirmed = false; }
            Console.WriteLine();

            if (!confirmed)
            {
                Console.WriteLine("  已取消，未做任何改动。");
                Pause(quiet);
                return 0;
            }
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("  （--yes：跳过确认，直接卸载）");
        }

        var failed = 0;
        var permissionProblem = false;
        foreach (var item in found)
        {
            Console.WriteLine("  正在卸载…（会显示一个进度窗口）");
            try
            {
                var startInfo = new ProcessStartInfo("msiexec.exe", $"/x {item.Code} /qb /norestart")
                {
                    UseShellExecute = false,
                };
                using var proc = Process.Start(startInfo);
                proc?.WaitForExit();
                var code = proc?.ExitCode ?? -1;
                if (proc is null || code != 0)
                {
                    failed++;
                    // 1925=权限不足 1730=权限不足 1603=致命错误（常见于权限问题）
                    if (code is 1925 or 1730 or 1603) permissionProblem = true;
                    Console.WriteLine("  ✗ 卸载失败，msiexec 退出码 " + (proc is null ? "?" : code.ToString()));
                }
                else
                {
                    Console.WriteLine("  ✓ 已卸载");
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine("  ✗ 调用 msiexec 失败：" + ex.Message);
            }
        }

        Console.WriteLine();
        if (failed == 0)
        {
            Console.WriteLine("  完成。程序与快捷方式已删除。");
            Console.WriteLine("  注意：你的 config.json（映射表配置）保留在安装目录里；想彻底清掉就手动删掉整个文件夹。");
        }
        else
        {
            Console.WriteLine("  有项目卸载失败。");
            if (permissionProblem && !isAdmin)
            {
                Console.WriteLine("  👉 看起来是权限不足：请关闭本窗口，右键本程序 →「以管理员身份运行」再试一次。");
            }
            else
            {
                Console.WriteLine("  👉 可以换成管理员身份重试（右键 →「以管理员身份运行」），");
                Console.WriteLine("     或到「设置 → 应用 → 已安装的应用」里手动卸载。");
            }
        }

        Pause(quiet);
        return failed == 0 ? 0 : 1;
    }

    /// <summary>当前进程是否以管理员（提升后）权限运行。</summary>
    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>在 HKCU / HKLM（64 位与 32 位视图）的卸载项里查找本程序，返回产品代码与安装位置。</summary>
    private static List<(string Code, string Location)> FindProducts()
    {
        var result = new List<(string Code, string Location)>();
        var roots = new (RegistryHive Hive, RegistryView View)[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
        };

        foreach (var (hive, view) in roots)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;

                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    using var item = uninstall.OpenSubKey(subKeyName);
                    if (item?.GetValue("DisplayName") is not string displayName) continue;
                    if (!string.Equals(displayName, ProductName, StringComparison.Ordinal)) continue;

                    var already = false;
                    foreach (var existing in result)
                    {
                        if (string.Equals(existing.Code, subKeyName, StringComparison.OrdinalIgnoreCase)) { already = true; break; }
                    }
                    if (already) continue;

                    result.Add((subKeyName, item.GetValue("InstallLocation") as string ?? string.Empty));
                }
            }
            catch
            {
                // 某个视图读不到就跳过，不影响其他视图
            }
        }

        return result;
    }

    private static void Pause(bool quiet)
    {
        if (quiet) return;
        Console.WriteLine();
        Console.Write("  按任意键退出…");
        try { Console.ReadKey(intercept: true); } catch { /* 非交互场景直接退出 */ }
        Console.WriteLine();
    }
}
