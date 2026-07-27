using System.Diagnostics;
using PacToolkits.Agents.Contracts.Agents;

namespace PacToolkits.Agents.Host;

/// <summary>
/// 常驻 Host：保持 Agents.exe 存活，监管 Modules/* 启停
///
/// 不自动挂载；由 Desktop 写入各模块 <see cref="AgentsPaths.ModuleControlFileName"/>
///（start / stop）与根目录 <see cref="AgentsPaths.HostControlFileName"/>（quit）
/// 每轮控制轮询 reconcile 磁盘清单，运行中增删 Modules 目录即可热发现
/// </summary>
internal static class Program
{
    // Desktop↔Host 控制文件轮询；读后即删，避免重复执行
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

        // 不自动挂载：等 Desktop 写入 start/stop 或 host.control quit
        while (!quit.IsSet)
        {
            // 先对齐磁盘清单，再读 control，避免新模块的 start 落在未知 slot 上
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
                        // 单模块启动失败不得拖垮 Host（settings 缺失 / 落盘竞态）
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

    /// <summary>
    /// 按 ScanModules 增补/刷新/移除 slot；移除前先停进程；entry 缺失的清单项不挂 slot
    /// </summary>
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
                // 进程在跑时不改 EntryPath，避免与存活进程脱节；停后再对齐
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

        // 从 --config 旁路推导 {ConfigDir}/agents/modules/<Id>/settings.json
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
            // 控制文件删除失败可忽略（下一轮轮询会再试）
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

    // 去掉 Host 专用 / 模块专用参数，避免原样转发给模块后再重复追加
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
            // 强制结束失败可忽略（进程可能已退出）
        }
    }
}
