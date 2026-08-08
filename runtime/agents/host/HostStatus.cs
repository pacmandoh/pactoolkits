using System.Diagnostics;
using PacToolkits.Agents.Contracts.Agents;

namespace PacToolkits.Agents.Host;

/// <summary>
/// 将槽位监督事实写成 StatusSnapshot（文件与管道推送字段）
/// </summary>
internal static class HostStatus
{
    public static AgentsStatus? Publish(
        string agentsDir,
        IReadOnlyList<ModuleSlotView> slots,
        HashSet<string> desired)
    {
        try
        {
            var modules = new List<AgentsStatusModule>(slots.Count);
            foreach (var slot in slots)
            {
                var alive = slot.Process is { HasExited: false };
                var ready = false;
                if (alive)
                {
                    ready = File.Exists(AgentsPaths.ModuleReadyPath(agentsDir, slot.Id));
                }

                var want = desired.Contains(slot.Id);
                var wire = new AgentsStatusModule
                {
                    Id = slot.Id,
                    Pid = alive ? slot.Process!.Id : null,
                    Ready = ready,
                    LastError = slot.LastError,
                    State = AgentsObserve.Module(
                        desired: want,
                        processAlive: alive,
                        ready: ready,
                        startFailed: slot.StartFailed,
                        lastError: slot.LastError),
                };

                if (slot.Catalog is { } cat)
                {
                    AgentsStatusCatalog.FillCatalog(wire, cat);
                }

                modules.Add(wire);
            }

            var status = AgentsStatus.Create(Environment.ProcessId, modules);
            AgentsStatus.Write(agentsDir, status);
            return status;
        }
        catch (Exception ex)
        {
            HostLog.Error("host.status.write_fail", ex.Message);
            return null;
        }
    }

    public static void Clear(string agentsDir)
        => AgentsStatus.TryDelete(agentsDir);

    public static void ClearModuleReady(string agentsDir, string moduleId)
    {
        try
        {
            var path = AgentsPaths.ModuleReadyPath(agentsDir, moduleId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 进程退出后残留就绪文件不影响 status 中的 Ready=false
        }
    }
}

/// <summary>
/// Host 循环槽位投影
/// </summary>
internal readonly record struct ModuleSlotView(
    string Id,
    Process? Process,
    string? LastError,
    bool StartFailed,
    ModuleDescriptor? Catalog);
