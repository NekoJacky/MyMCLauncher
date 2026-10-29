using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyMCLauncher.Services;

/// <summary>
/// 统一的 JSON 读写入口，负责原子写入与损坏文件处理。
/// </summary>
public sealed class JsonStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 发生非致命问题时触发，例如文件损坏或读取失败。
    /// </summary>
    public event Action<string>? Warning;

    /// <summary>
    /// 读取 JSON 文件。
    /// 文件不存在、损坏或读取失败时返回 <paramref name="defaultFactory"/> 的结果。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="path">文件路径。</param>
    /// <param name="defaultFactory">无法读取时使用的默认值工厂。</param>
    public T Load<T>(string path, Func<T>? defaultFactory = null)
    {
        if (TryLoad(path, out T? value))
        {
            return value!;
        }

        return defaultFactory is not null ? defaultFactory() : default!;
    }

    /// <summary>
    /// 尝试读取 JSON 文件。
    /// 文件不存在、损坏或读取失败时返回 <see langword="false"/>。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="path">文件路径。</param>
    /// <param name="value">读取结果。</param>
    public bool TryLoad<T>(string path, out T? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        value = default;

        if (!File.Exists(path))
        {
            return false;
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportWarning($"读取文件失败：{path}，{ex.Message}");
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(json, SerializerOptions);
            return value is not null;
        }
        catch (JsonException ex)
        {
            BackupCorruptFile(path, ex);
            return false;
        }
        catch (NotSupportedException ex)
        {
            ReportWarning($"不支持的类型 {typeof(T).FullName}：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 原子写入 JSON 文件：先写临时文件，再覆盖目标文件。
    /// </summary>
    /// <typeparam name="T">值类型。</typeparam>
    /// <param name="path">文件路径。</param>
    /// <param name="value">要写入的值。</param>
    public void Save<T>(string path, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(value, SerializerOptions);
        var tempPath = fullPath + ".tmp";

        try
        {
            File.WriteAllText(tempPath, json, Utf8NoBom);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[JsonStore] 删除临时文件失败：{path}，{ex.Message}");
        }
    }

    private void BackupCorruptFile(string path, JsonException exception)
    {
        var backupPath = BuildBackupPath(path);

        try
        {
            File.Move(path, backupPath);
            ReportWarning($"文件内容无效，已备份为 {backupPath}：{exception.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportWarning($"文件内容无效且备份失败：{path}，{ex.Message}");
        }
    }

    private static string BuildBackupPath(string path)
    {
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var candidate = $"{path}.corrupt-{stamp}";
        return File.Exists(candidate)
            ? $"{path}.corrupt-{stamp}-{Guid.NewGuid():N}"
            : candidate;
    }

    private void ReportWarning(string message)
    {
        Debug.WriteLine($"[JsonStore] {message}");
        Warning?.Invoke(message);
    }
}
