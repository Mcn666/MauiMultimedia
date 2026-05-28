using System.Collections.Concurrent;
using System.Text.Json;

namespace MauiMultimedia.Shell.Services;

/// <summary>
/// 文件路径 → 别名映射服务，持久化到 MAUI Preferences。
/// 别名不影响实际文件名，仅在界面中优先显示。
/// </summary>
public class AliasService
{
    private const string PrefKey = "filebrowser-aliases";
    private ConcurrentDictionary<string, string> _aliases;

    public AliasService()
    {
        try
        {
            var json = Preferences.Get(PrefKey, "{}");
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            _aliases = new ConcurrentDictionary<string, string>(
                dict ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _aliases = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>获取文件别名，没有别名时返回 null</summary>
    public string? GetAlias(string filePath)
    {
        return _aliases.TryGetValue(filePath, out var alias) ? alias : null;
    }

    /// <summary>获取显示名称：有别名则显示别名，否则显示文件名</summary>
    public string GetDisplayName(string filePath)
    {
        return GetAlias(filePath) ?? Path.GetFileName(filePath);
    }

    /// <summary>设置别名（空字符串或 null 移除别名）</summary>
    public void SetAlias(string filePath, string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            _aliases.TryRemove(filePath, out _);
        else
            _aliases[filePath] = alias.Trim();
        Save();
    }

    /// <summary>移除指定路径的别名</summary>
    public void RemoveAlias(string filePath) => SetAlias(filePath, null);

    private void Save()
    {
        var json = JsonSerializer.Serialize(
            new Dictionary<string, string>(_aliases, StringComparer.OrdinalIgnoreCase));
        Preferences.Set(PrefKey, json);
    }
}
