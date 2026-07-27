using System.Diagnostics;
using PacToolkits.Agents.Contracts.Agents;

namespace PacToolkits.Agents.Host;

/// <summary>
/// Agents 常驻进程，负责模块发现、控制文件消费和子进程监管
///
/// 模块仅响应 Desktop 写入的 start 或 stop 命令；Host 退出由根目录 quit 命令控制
/// </summary>
internal static class Program
{
    // 控制文件无确认通道，消费后立即删除以保证命令至多执行一次
    private static readonly TimeSpan ControlPoll = TimeSpan.FromMilliseconds(250);

    private sealed class ModuleSlot
    {
        public required string Id { get; init; }
        public required string EntryPath { get; set; }
        public required string ModuleDir { get; set; }
        public required string ControlPath { get; set; }
        public Process? Process { get; set; }
    }

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        var childArgs = FilterHostArgs(args).ToList();
        var slots = new List<ModuleSlot>();
        ReconcileSlots(slots, baseDir);

        var hostControlPath = AgentsPaths.HostControlPath(baseDir);
        var quit = new ManualResetEventSlim(false);

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            quit.Set();
        };

        // Host 不根据 Enabled 推断启动意图，模块生命周期仅由 Desktop 控制命令驱动
        while (!quit.IsSet)
        {
            // 先更新模块槽位，确保新部署模块的首条控制命令可以在同一轮处理
            ReconcileSlots(slots, baseDir);

            foreach (var slot in slots)
            {
                if (slot.Process is not null && slot.Process.HasExited)
                {
                    slot.Process.Dispose();
                    slot.Process = null;
                }
            }

            var hostCommand = TryReadControl(hostControlPath);
            if (hostCommand is not null)
            {
                TryDelete(hostControlPath);
                if (hostCommand == "quit")
                {
                    StopAll(slots);
                    return 0;
                }
            }

            foreach (var slot in slots)
            {
                var command = TryReadControl(slot.ControlPath);
                if (command is null)
                {
                    continue;
                }

                TryDelete(slot.ControlPath);
                switch (command)
                {
                    case "stop":
                        StopModule(slot);
                        break;
                    case "start":
                        // 单模块配置或部署失败不得中断其他模块的控制循环
                        try
                        {
                            if (slot.Process is null || slot.Process.HasExited)
                            {
                                slot.Process?.Dispose();
                                slot.Process = StartModule(slot, childArgs);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"Module start failed ({slot.Id}): {ex.Message}");
                            slot.Process = null;
                        }

                        break;
                }
            }

            quit.Wait(ControlPoll);
        }

        StopAll(slots);
        return 0;
    }

    private static void ReconcileSlots(List<ModuleSlot> slots, string agentsDir)
    {
        var desired = AgentsPath.ScanModules(agentsDir);
        var byId = new Dictionary<string, ModuleSlot>(StringComparer.Ordinal);
        foreach (var slot in slots)
        {
            byId[slot.Id] = slot;
        }

        var keepIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in desired)
        {
            var entryPath = AgentsPath.TryResolveModuleEntryPath(agentsDir, module.Id);
            if (entryPath is null)
            {
                Console.Error.WriteLine($"Invalid module entry: {module.Id}");
                continue;
            }

            if (!File.Exists(entryPath))
            {
                Console.Error.WriteLine($"Missing module entry: {entryPath}");
                continue;
            }

            keepIds.Add(module.Id);
            var controlPath = AgentsPaths.ModuleControlPath(agentsDir, module.Id);
            if (byId.TryGetValue(module.Id, out var existing))
            {
                // 运行中槽位保留原入口，确保进程引用和后续停止操作保持一致
                if (existing.Process is null || existing.Process.HasExited)
                {
                    existing.EntryPath = entryPath;
                    existing.ModuleDir = module.Directory;
                    existing.ControlPath = controlPath;
                }

                continue;
            }

            slots.Add(new ModuleSlot
            {
                Id = module.Id,
                EntryPath = entryPath,
                ModuleDir = module.Directory,
                ControlPath = controlPath,
            });
            Console.WriteLine($"Module discovered: {module.Id}");
        }

        for (var i = slots.Count - 1; i >= 0; i--)
        {
            var slot = slots[i];
            if (keepIds.Contains(slot.Id))
            {
                continue;
            }

            StopModule(slot);
            slots.RemoveAt(i);
            Console.WriteLine($"Module removed: {slot.Id}");
        }
    }

    private static void StopAll(List<ModuleSlot> slots)
    {
        foreach (var slot in slots)
        {
            StopModule(slot);
        }
    }

    private static Process? StartModule(ModuleSlot slot, IReadOnlyList<string> childArgs)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = slot.EntryPath,
            WorkingDirectory = slot.ModuleDir,
            UseShellExecute = false,
        };
        foreach (var arg in childArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // 用户模块配置与 Desktop 配置共享父目录，避免 Host 读取或复制 Desktop 配置内容
        var settingsPath = TryResolveModuleSettingsPath(childArgs, slot.Id)
            ?? throw new InvalidOperationException(
                $"Cannot resolve --module-settings for module {slot.Id} (missing --config)");

        if (!File.Exists(settingsPath))
        {
            throw new InvalidOperationException($"Missing module settings: {settingsPath}");
        }

        startInfo.ArgumentList.Add(AgentsPaths.ModuleSettingsArgName);
        startInfo.ArgumentList.Add(settingsPath);

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"Failed to start module process: {slot.EntryPath}");
        }

        return process;
    }

    private static string? TryResolveModuleSettingsPath(IReadOnlyList<string> childArgs, string moduleId)
    {
        var configPath = GetArgValue(childArgs, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return null;
        }

        try
        {
            var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath));
            if (string.IsNullOrWhiteSpace(configDir))
            {
                return null;
            }

            return AgentsPaths.ModuleSettingsPath(configDir, moduleId);
        }
        catch
        {
            return null;
        }
    }

    private static void StopModule(ModuleSlot slot)
    {
        if (slot.Process is null)
        {
            return;
        }

        try
        {
            TryKill(slot.Process);
        }
        finally
        {
            slot.Process.Dispose();
            slot.Process = null;
        }
    }

    private static string? TryReadControl(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path).Trim();
            if (text.Length == 0)
            {
                return null;
            }

            var line = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
            return line.ToLowerInvariant() switch
            {
                "stop" => "stop",
                "start" => "start",
                "quit" => "quit",
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 删除失败时保留命令供下一轮重试，避免错误标记为已消费
        }
    }

    private static string? GetArgValue(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Count)
            {
                return null;
            }

            return args[i + 1];
        }

        return null;
    }

    // 过滤 Host 自有参数，保证每个模块只接收一份由 Host 生成的模块配置参数
    private static IReadOnlyList<string> FilterHostArgs(string[] args)
    {
        var result = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], AgentsPaths.ModuleSettingsArgName, StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            result.Add(args[i]);
        }

        return result;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2500);
            }
        }
        catch
        {
            // 终止请求与进程自然退出可能并发，失败不代表仍有存活进程
        }
    }
}
