namespace PacToolkits.Agents.Contracts.Commands;

/// <summary>
/// Agents 启停命令结果；SuppressToast 用于避免与上层已展示的错误重复弹窗
/// </summary>
public readonly record struct AgentsCommandResult(bool Ok, string Message, bool SuppressToast = false);
