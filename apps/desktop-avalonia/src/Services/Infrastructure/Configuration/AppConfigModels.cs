using System;
using System.Collections.Generic;
using PacToolkits.Agents.Contracts.Models;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Configuration;

/// <summary>Desktop 持久化配置根对象</summary>
public sealed class AppConfigRoot
{
    public int SchemaVersion { get; set; } = 2;
    public PgOptions Postgres { get; set; } = new();
    public Dictionary<string, string> ClientAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public TraceCodeValidationOptions TraceCodeValidation { get; set; } = new();
    public AgentsOptions Agents { get; set; } = new();
    public PacApiOptions PacApi { get; set; } = new();
    public MsfxApiOptions MsfxApi { get; set; } = new();
    public UiBehaviorOptions UiBehavior { get; set; } = new();
    public UpdateOptions Update { get; set; } = new();
    public LoggingOptions Logging { get; set; } = new();
}

/// <summary>Desktop 界面行为配置</summary>
public sealed class UiBehaviorOptions
{
    public bool MinimizeToTrayOnClose { get; set; } = true;
}

/// <summary>Desktop 日志输出配置</summary>
public sealed class LoggingOptions
{
    public bool Enabled { get; set; } = true;
    public string MinimumLevel { get; set; } = "Error";
    public int RetentionDays { get; set; } = 14;
    public int MaxFileSizeMb { get; set; } = 20;
    public string LogDirectory { get; set; } = string.Empty;
}
