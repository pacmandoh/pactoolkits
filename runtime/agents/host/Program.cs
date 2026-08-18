using System.Diagnostics;
using PacToolkits.Agents.Contracts.Agents;

namespace PacToolkits.Agents.Host;

/// <summary>
/// Agents 常驻进程：发现、desired reconcile、命名管道 IPC、热更、status 发布
///
/// 只 reconcile desired ↔ 模块进程，不连库、不判 schema；库策略由 Desktop 过滤后再写入 desired
/// </summary>
internal static class Program
{
    private static readonly TimeSpan ControlPoll = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CatalogScanPeriod = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BinaryCheckPeriod = TimeSpan.FromSeconds(1);

    private sealed class ModuleSlot
    {
        public required string Id { get; init; }
        public required string EntryPath { get; set; }
        public required string ModuleDir { get; set; }
        public Process? Process { get; set; }
        public ModuleDescriptor? Catalog { get; set; }
        public string? LastError { get; set; }
        public bool StartFailed { get; set; }
        public DateTimeOffset? LaunchUtc { get; set; }
        public bool SawReady { get; set; }
    }

    private static int Main(string[] args)
    {
        try
        {
            HostLog.Init(GetArgValue(args, "--config"));
            return Run(args);
        }
        catch (Exception ex)
        {
            HostLog.Error("host.crash", ex.Message);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        var childArgs = FilterHostArgs(args).ToList();
        var slots = new List<ModuleSlot>();
        var binaries = new HostModuleBinary();
        using var ipc = new HostIpc(baseDir);
        ReconcileSlots(slots, baseDir);

        var quit = new ManualResetEventSlim(false);

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            quit.Set();
        };

        HostLog.Info("host.ready", "Agents Host control loop started");
        ipc.Start();
        PublishStatus(baseDir, slots, ipc, ipc.ReadDesiredIds());

        var lastCatalogScan = DateTimeOffset.UtcNow;
        var lastBinaryCheck = DateTimeOffset.MinValue;

        while (!quit.IsSet && !ipc.QuitRequested)
        {
            ipc.DrainActions();
            var now = DateTimeOffset.UtcNow;
            if (now - lastCatalogScan >= CatalogScanPeriod)
            {
                ReconcileSlots(slots, baseDir);
                lastCatalogScan = now;
            }

            var desired = ipc.ReadDesiredIds();
            ReapExited(slots, baseDir, desired);
            ReconcileDesired(slots, baseDir, childArgs, ipc);
            NoteReadyAndWatchdog(slots, baseDir, desired);

            if (now - lastBinaryCheck >= BinaryCheckPeriod)
            {
                binaries.Sync(slots.Select(s => (s.Id, s.EntryPath)).ToList());
                foreach (var moduleId in binaries.DetectReloads(
                             slots.Select(s => (s.Id, Alive: s.Process is { HasExited: false })).ToList()))
                {
                    var slot = slots.FirstOrDefault(s => string.Equals(s.Id, moduleId, StringComparison.Ordinal));
                    if (slot is null)
                    {
                        continue;
                    }

                    HostLog.Info(
                        "host.module.binary_reload",
                        $"Module binary changed, restarting: {moduleId}",
                        new { moduleId });
                    StopModule(slot, baseDir, clearError: true);
                    TryStartSlot(slot, childArgs, source: "binary_reload");
                }

                lastBinaryCheck = now;
            }

            PublishStatus(baseDir, slots, ipc, desired);
            quit.Wait(ControlPoll);
        }

        HostLog.Info("host.shutdown", "Host control loop ending");
        StopAll(slots, baseDir);
        HostStatus.Clear(baseDir);
        HostDesired.Clear(baseDir);
        return 0;
    }

    private static void ReapExited(List<ModuleSlot> slots, string baseDir, HashSet<string> desired)
    {
        foreach (var slot in slots)
        {
            if (slot.Process is null || !slot.Process.HasExited)
            {
                continue;
            }

            var exitCode = slot.Process.ExitCode;
            HostLog.Info(
                "host.module.exited",
                $"Module process exited: {slot.Id}",
                new { moduleId = slot.Id, exitCode });
            slot.Process.Dispose();
            slot.Process = null;
            HostStatus.ClearModuleReady(baseDir, slot.Id);

            if (!desired.Contains(slot.Id))
            {
                slot.LaunchUtc = null;
                continue;
            }

            // desired 下意外退出：写入 Snapshot 失败态，避免 Desktop 本地超时造状态
            if (!slot.SawReady || exitCode != 0)
            {
                slot.StartFailed = true;
                slot.LastError = slot.SawReady
                    ? $"Module process exited with code {exitCode}"
                    : $"Module process exited before ready (code {exitCode})";
            }

            slot.LaunchUtc = null;
        }
    }

    private static void NoteReadyAndWatchdog(List<ModuleSlot> slots, string agentsDir, HashSet<string> desired)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var slot in slots)
        {
            var want = desired.Contains(slot.Id);
            var alive = slot.Process is { HasExited: false };
            if (!want || !alive)
            {
                continue;
            }

            if (File.Exists(AgentsPaths.ModuleReadyPath(agentsDir, slot.Id)))
            {
                slot.SawReady = true;
                continue;
            }

            if (slot.StartFailed || slot.LaunchUtc is null)
            {
                continue;
            }

            if (now - slot.LaunchUtc < AgentsObserve.ModuleReadyTimeout)
            {
                continue;
            }

            slot.StartFailed = true;
            slot.LastError = "Module did not become ready in time";
            HostLog.Error(
                "host.module.ready_timeout",
                slot.LastError,
                new { moduleId = slot.Id, timeoutSec = AgentsObserve.ModuleReadyTimeout.TotalSeconds });
            StopModule(slot, agentsDir, clearError: false);
        }
    }

    private static void ReconcileDesired(
        List<ModuleSlot> slots,
        string baseDir,
        IReadOnlyList<string> childArgs,
        HostIpc ipc)
    {
        var desired = ipc.ReadDesiredIds();
        foreach (var slot in slots)
        {
            var want = desired.Contains(slot.Id);
            var alive = slot.Process is { HasExited: false };
            if (!want)
            {
                if (alive)
                {
                    StopModule(slot, baseDir, clearError: true);
                }
                else
                {
                    slot.StartFailed = false;
                    slot.LastError = null;
                    slot.LaunchUtc = null;
                    slot.SawReady = false;
                }
            }
            else if (!alive)
            {
                TryStartSlot(slot, childArgs, source: "desired");
            }
        }
    }

    private static void TryStartSlot(ModuleSlot slot, IReadOnlyList<string> childArgs, string source)
    {
        try
        {
            if (slot.Process is { HasExited: false })
            {
                return;
            }

            // 同一 desired 周期内启动失败后勿热循环疯狂重试
            if (slot.StartFailed && string.Equals(source, "desired", StringComparison.Ordinal))
            {
                return;
            }

            slot.Process?.Dispose();
            slot.LastError = null;
            slot.StartFailed = false;
            slot.SawReady = false;
            slot.Process = StartModule(slot, childArgs);
            slot.LaunchUtc = DateTimeOffset.UtcNow;
            HostLog.Info(
                "host.module.start",
                $"Module started: {slot.Id}",
                new { moduleId = slot.Id, pid = slot.Process?.Id, source });
        }
        catch (Exception ex)
        {
            HostLog.Error(
                "host.module_start.fail",
                ex.Message,
                new { moduleId = slot.Id, source });
            slot.Process = null;
            slot.LaunchUtc = null;
            slot.SawReady = false;
            slot.LastError = ex.Message;
            slot.StartFailed = true;
        }
    }

    private static void PublishStatus(
        string agentsDir,
        List<ModuleSlot> slots,
        HostIpc ipc,
        HashSet<string> desired)
    {
        var views = new ModuleSlotView[slots.Count];
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            views[i] = new ModuleSlotView(
                slot.Id,
                slot.Process,
                slot.LastError,
                slot.StartFailed,
                slot.Catalog);
        }

        var status = HostStatus.Publish(agentsDir, views, desired);
        if (status is not null)
        {
            ipc.PushStatus(status);
        }
    }

    private static void ReconcileSlots(List<ModuleSlot> slots, string agentsDir)
    {
        var catalog = AgentsPath.ScanModules(agentsDir);
        var byId = new Dictionary<string, ModuleSlot>(StringComparer.Ordinal);
        foreach (var slot in slots)
        {
            byId[slot.Id] = slot;
        }

        var keepIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in catalog)
        {
            var entryPath = AgentsPath.TryResolveModuleEntryPath(module);
            if (entryPath is null)
            {
                HostLog.Error("host.module_entry.invalid", $"Invalid module entry: {module.Id}", new { moduleId = module.Id });
                continue;
            }

            if (!File.Exists(entryPath))
            {
                HostLog.Error(
                    "host.module_entry.missing",
                    $"Missing module entry: {entryPath}",
                    new { moduleId = module.Id, entryPath });
                continue;
            }

            keepIds.Add(module.Id);
            if (byId.TryGetValue(module.Id, out var existing))
            {
                existing.Catalog = module;
                if (existing.Process is null || existing.Process.HasExited)
                {
                    existing.EntryPath = entryPath;
                    existing.ModuleDir = module.Directory;
                }

                continue;
            }

            slots.Add(new ModuleSlot
            {
                Id = module.Id,
                EntryPath = entryPath,
                ModuleDir = module.Directory,
                Catalog = module,
            });
            HostLog.Info("host.module.discovered", $"Module discovered: {module.Id}", new { moduleId = module.Id });
        }

        for (var i = slots.Count - 1; i >= 0; i--)
        {
            var slot = slots[i];
            if (keepIds.Contains(slot.Id))
            {
                continue;
            }

            StopModule(slot, agentsDir, clearError: true);
            slots.RemoveAt(i);
            HostLog.Info("host.module.removed", $"Module removed: {slot.Id}", new { moduleId = slot.Id });
        }
    }

    private static void StopAll(List<ModuleSlot> slots, string agentsDir)
    {
        foreach (var slot in slots)
        {
            StopModule(slot, agentsDir, clearError: true);
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

    private static void StopModule(ModuleSlot slot, string agentsDir, bool clearError)
    {
        if (slot.Process is null)
        {
            if (clearError)
            {
                slot.LastError = null;
                slot.StartFailed = false;
                slot.LaunchUtc = null;
                slot.SawReady = false;
            }

            return;
        }

        var pid = slot.Process.Id;
        try
        {
            TryKill(slot.Process);
        }
        finally
        {
            slot.Process.Dispose();
            slot.Process = null;
            slot.LaunchUtc = null;
            if (clearError)
            {
                slot.LastError = null;
                slot.StartFailed = false;
                slot.SawReady = false;
            }

            HostStatus.ClearModuleReady(agentsDir, slot.Id);
            HostLog.Info(
                "host.module.stop",
                $"Module stopped: {slot.Id}",
                new { moduleId = slot.Id, pid });
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
            // 终止请求与进程自然退出可能并发
        }
    }
}
