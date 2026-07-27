using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

/// <summary>
/// 同目录临时文件写入与原子替换
/// </summary>
internal static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        var tempPath = CreateTempPath(path);
        try
        {
            File.WriteAllText(tempPath, content);
            Replace(tempPath, path);
        }
        finally
        {
            Cleanup(tempPath, path);
        }
    }

    public static async Task WriteAllTextAsync(string path, string content, CancellationToken ct)
    {
        var tempPath = CreateTempPath(path);
        try
        {
            await File.WriteAllTextAsync(tempPath, content, ct).ConfigureAwait(false);
            Replace(tempPath, path);
        }
        finally
        {
            Cleanup(tempPath, path);
        }
    }

    public static void CopyNew(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"源文件不存在：{sourcePath}", sourcePath);
        }

        var tempPath = CreateTempPath(targetPath);
        try
        {
            File.Copy(sourcePath, tempPath, overwrite: false);
            File.Move(tempPath, targetPath, overwrite: false);
        }
        finally
        {
            Cleanup(tempPath, targetPath);
        }
    }

    private static string CreateTempPath(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("配置目录无效");
        }

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
    }

    private static void Replace(string tempPath, string targetPath)
    {
        try
        {
            if (File.Exists(targetPath))
            {
                File.Replace(tempPath, targetPath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }
        }
        catch
        {
            File.Move(tempPath, targetPath, overwrite: true);
        }
    }

    private static void Cleanup(string tempPath, string targetPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn(
                "AtomicFile",
                "file.atomic_cleanup.fail",
                "清理临时文件失败",
                ex,
                new { tempPath, targetPath });
        }
    }
}
