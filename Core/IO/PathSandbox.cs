using System;
using System.IO;

namespace MauiMultimedia.Core.IO;

/// <summary>
/// 路径沙盒守卫：确保查看器产生的任何输出（临时文件、解压产物、转换结果等）
/// 都落在应用私有目录（沙盒）之内，绝不落到系统 Temp 或用户存储等沙盒之外。
/// 纯 System.IO 实现，无平台依赖，可置于 Core 类库。
/// </summary>
public static class PathSandbox
{
    /// <summary>
    /// 规范化一个 "scope" 名，去掉任何路径分隔符与非法文件名字符，
    /// 防止通过 scope 注入子路径（如 "DdsDecode/../evil"）。
    /// </summary>
    public static string SanitizeScope(string scope)
    {
        var s = string.IsNullOrWhiteSpace(scope) ? "scratch" : scope.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }

    /// <summary>
    /// 判断 path 是否位于 root 之内（含 root 自身）。
    /// 仅做字符串/分隔符规范化（不解析符号链接），足以拦截 ../ 越界与绝对路径逃逸。
    /// </summary>
    public static bool IsWithin(string root, string path)
    {
        try
        {
            var normalizedRoot = NormalizeRoot(root);
            if (string.IsNullOrEmpty(normalizedRoot)) return false;
            var fullPath = Path.GetFullPath(path);
            if (string.Equals(fullPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                return true;
            return fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 校验 path 位于 root 之内；否则抛出 <see cref="UnauthorizedAccessException"/>。
    /// 返回规范化后的绝对路径，便于调用方直接使用。
    /// 用于"路径由外部输入拼接而成"的高危点（如压缩包条目名、用户提供的相对路径），
    /// 在真正落盘前拦截越界写入。
    /// </summary>
    public static string EnsureWithin(string root, string path)
    {
        var normalizedRoot = NormalizeRoot(root);
        if (string.IsNullOrEmpty(normalizedRoot))
            throw new UnauthorizedAccessException($"非法根目录：{root}");

        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception ex) { throw new UnauthorizedAccessException($"非法路径：{path}", ex); }

        if (string.Equals(fullPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return fullPath;
        if (fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return fullPath;

        throw new UnauthorizedAccessException(
            $"路径 '{path}' 试图写出应用私有目录之外（根目录：{normalizedRoot}），已阻止。");
    }

    private static string NormalizeRoot(string root)
    {
        if (string.IsNullOrEmpty(root)) return string.Empty;
        var r = Path.GetFullPath(root);
        // 统一结尾分隔符，便于 StartsWith 比较（比较时再补分隔符，这里仅返回无尾斜杠形式）
        return r.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
