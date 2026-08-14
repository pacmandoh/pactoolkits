using System;
using System.Collections.Generic;
using PacToolkits.Agents.Contracts.Models;
using AppDtos = PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.Services.Integration.Agents;

/// <summary>
/// 在 Agents 持久化模型与 Application DTO 之间执行无业务规则的类型映射
/// </summary>
internal static class AgentsContractMapper
{
    public static AppDtos.AgentsConfigDto ToApplication(AgentsOptions source)
        => new()
        {
            ExecutablePath = source.ExecutablePath,
            ProcessName = source.ProcessName,
            Modules = ToApplicationModules(source.Modules),
        };

    public static AgentsOptions ToContract(AppDtos.AgentsConfigDto source)
        => new()
        {
            ExecutablePath = source.ExecutablePath,
            ProcessName = source.ProcessName,
            Modules = ToContractModules(source.Modules),
        };

    private static Dictionary<string, AppDtos.ModuleOptionsDto> ToApplicationModules(
        Dictionary<string, ModuleOptions> source)
    {
        var result = new Dictionary<string, AppDtos.ModuleOptionsDto>(StringComparer.Ordinal);
        foreach (var (id, options) in source)
        {
            result[id] = new AppDtos.ModuleOptionsDto { Enabled = options.Enabled };
        }

        return result;
    }

    private static Dictionary<string, ModuleOptions> ToContractModules(
        Dictionary<string, AppDtos.ModuleOptionsDto> source)
    {
        var result = new Dictionary<string, ModuleOptions>(StringComparer.Ordinal);
        foreach (var (id, options) in source)
        {
            result[id] = new ModuleOptions { Enabled = options.Enabled };
        }

        return result;
    }
}
