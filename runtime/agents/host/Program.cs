using System.Diagnostics;
using PacToolkits.Agents.Contracts.Agents;

namespace PacToolkits.Agents.Host;

/// <summary>
/// 常驻 Host：保持 Agents.exe 存活，按 Modules/&lt;Id&gt; 启停模块
///
/// 不自动挂载；由 Desktop 写入 <see cref="AgentsPaths.ModuleControlFileName"/>
///（<c>start</c> / <c>stop</c> / <c>quit</c>）
/// </summary>
internal static class Program
{
    // Desktop↔Host 控制文件轮询；读后即删，避免重复执行
    private static readonly TimeSpan ControlPoll = TimeSpan.FromMilliseconds(250);

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
        var moduleId = GetArgValue(args, "--module") ?? AgentsPaths.InjectorModuleId;
        var baseDir = AppContext.BaseDirectory;
        var moduleDir = AgentsPaths.ModuleDir(baseDir, moduleId);
        var manifestPath = AgentsPaths.ModuleManifestPath(baseDir, moduleId);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"Missing module manifest: {manifestPath}");
        }

        var entryFileName = AgentsPath.TryReadModuleEntryFileName(manifestPath)
            ?? throw new InvalidOperationException("module.json is missing entry.windows-x64");
        var entryPath = Path.Combine(moduleDir, entryFileName);
        if (!File.Exists(entryPath))
        {
            throw new InvalidOperationException($"Missing module entry: {entryPath}");
        }

        var childArgs = FilterHostArgs(args).ToList();
        var controlPath = AgentsPaths.ModuleControlPath(baseDir, moduleId);
        var quit = new ManualResetEventSlim(false);
        Process? module = null;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            quit.Set();
        };

        // 不自动挂载：等 Desktop 在 Host 起来后写入 start/stop/quit
        while (!quit.IsSet)
        {
            if (module is not null && module.HasExited)
            {
                module.Dispose();
                module = null;
            }

            var command = TryReadControl(controlPath);
            if (command is not null)
            {
                TryDelete(controlPath);
                switch (command)
                {
                    case "stop":
                        StopModule(ref module);
                        break;
                    case "start":
                        if (module is null || module.HasExited)
                        {
                            module?.Dispose();
                            module = StartModule(entryPath, moduleDir, childArgs);
                        }

                        break;
                    case "quit":
                        StopModule(ref module);
                        return 0;
                }
            }

            quit.Wait(ControlPoll);
        }

        StopModule(ref module);
        return 0;
    }

    private static Process? StartModule(string entryPath, string moduleDir, IReadOnlyList<string> childArgs)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = entryPath,
            WorkingDirectory = moduleDir,
            UseShellExecute = false,
        };
        foreach (var arg in childArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"Failed to start module process: {entryPath}");
        }

        return process;
    }

    private static void StopModule(ref Process? module)
    {
        if (module is null)
        {
            return;
        }

        try
        {
            TryKill(module);
        }
        finally
        {
            module.Dispose();
            module = null;
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

    private static string? GetArgValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                return null;
            }

            return args[i + 1];
        }

        return null;
    }

    // 去掉 Host 专用参数，避免传给模块进程
    private static IReadOnlyList<string> FilterHostArgs(string[] args)
    {
        var result = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--module", StringComparison.OrdinalIgnoreCase))
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
