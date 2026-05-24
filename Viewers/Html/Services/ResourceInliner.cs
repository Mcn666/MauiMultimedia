using System.Text.RegularExpressions;

namespace MauiMultimedia.Viewers.Html.Services;

/// <summary>
/// 将 HTML 中引用的外部资源（CSS/JS/图片）内联为 data:URI 或 inline style，
/// 使 srcdoc iframe 能正确渲染页面。
/// </summary>
public static partial class ResourceInliner
{
    /// <summary>
    /// 内联 HTML 文件中所有可解析的外部资源引用
    /// </summary>
    public static string Inline(string html, string baseDir)
    {
        // 1. 内联 <link rel="stylesheet" href="...">
        html = InlineCssLinks(html, baseDir);

        // 2. 内联 <img src="...">（png/jpg/gif/svg/webp）
        html = InlineImages(html, baseDir);

        // 3. 内联 <script src="...">
        html = InlineScripts(html, baseDir);

        // 4. 内联 CSS 中的 url(...) 引用
        html = InlineCssUrlRefs(html, baseDir);

        return html;
    }

    private static string InlineCssLinks(string html, string baseDir)
    {
        return LinkCssRegex().Replace(html, match =>
        {
            var href = match.Groups[1].Value;
            var path = ResolvePath(href, baseDir);
            if (path == null || !File.Exists(path))
                return match.Value; // 保留原标签

            try
            {
                var css = File.ReadAllText(path);
                // 替换 CSS 中的 url(...) 引用
                css = InlineCssUrlPaths(css, Path.GetDirectoryName(path)!);
                return $"<style>\n{css}\n</style>";
            }
            catch { return match.Value; }
        });
    }

    private static string InlineImages(string html, string baseDir)
    {
        return ImgSrcRegex().Replace(html, match =>
        {
            var src = match.Groups[1].Value;
            if (src.StartsWith("data:")) return match.Value; // 已经是内联

            var path = ResolvePath(src, baseDir);
            if (path == null || !File.Exists(path))
                return match.Value;

            try
            {
                var mime = GetMime(Path.GetExtension(path));
                var bytes = File.ReadAllBytes(path);
                var b64 = Convert.ToBase64String(bytes);
                return match.Value.Replace($"src=\"{src}\"", $"src=\"data:{mime};base64,{b64}\"")
                    .Replace($"src='{src}'", $"src=\"data:{mime};base64,{b64}\"");
            }
            catch { return match.Value; }
        });
    }

    private static string InlineScripts(string html, string baseDir)
    {
        return ScriptSrcRegex().Replace(html, match =>
        {
            var src = match.Groups[1].Value;
            if (src.StartsWith("data:")) return match.Value;

            var path = ResolvePath(src, baseDir);
            if (path == null || !File.Exists(path))
                return match.Value;

            try
            {
                var content = File.ReadAllText(path);
                return $"<script>\n{content}\n</script>";
            }
            catch { return match.Value; }
        });
    }

    /// <summary>
    /// 在 style 标签和内联样式中替换 url(...) 引用
    /// </summary>
    private static string InlineCssUrlRefs(string html, string baseDir)
    {
        return CssUrlRegex().Replace(html, match =>
        {
            var url = match.Groups[1].Value.Trim('\'', '"');
            if (url.StartsWith("data:") || url.StartsWith("http"))
                return match.Value;

            var path = ResolvePath(url, baseDir);
            if (path == null || !File.Exists(path))
                return match.Value;

            try
            {
                var mime = GetMime(Path.GetExtension(path));
                var bytes = File.ReadAllBytes(path);
                var b64 = Convert.ToBase64String(bytes);
                return $"url(\"data:{mime};base64,{b64}\")";
            }
            catch { return match.Value; }
        });
    }

    /// <summary>
    /// 内联一整个 CSS 文件内部的 url(...) 路径
    /// </summary>
    private static string InlineCssUrlPaths(string css, string cssDir)
    {
        return CssUrlRegex().Replace(css, match =>
        {
            var url = match.Groups[1].Value.Trim('\'', '"');
            if (url.StartsWith("data:") || url.StartsWith("http"))
                return match.Value;

            var path = ResolvePath(url, cssDir);
            if (path == null || !File.Exists(path))
                return match.Value;

            try
            {
                var mime = GetMime(Path.GetExtension(path));
                var bytes = File.ReadAllBytes(path);
                var b64 = Convert.ToBase64String(bytes);
                return $"url(\"data:{mime};base64,{b64}\")";
            }
            catch { return match.Value; }
        });
    }

    private static string? ResolvePath(string href, string baseDir)
    {
        // 跳过绝对 URL 和 data: URI
        if (href.StartsWith("http://") || href.StartsWith("https://") ||
            href.StartsWith("data:") || href.StartsWith("//"))
            return null;

        // HTML 解码（&amp; → & 等）
        href = System.Net.WebUtility.HtmlDecode(href);

        // 处理 file:/// 路径
        if (href.StartsWith("file:///"))
        {
            return Uri.UnescapeDataString(href[8..].Replace('/', Path.DirectorySeparatorChar));
        }

        // 组合相对路径
        var combined = Path.GetFullPath(Path.Combine(baseDir, href));
        return File.Exists(combined) ? combined : null;
    }

    private static string GetMime(string ext) => ext.ToLowerInvariant() switch
    {
        ".css" => "text/css",
        ".js" => "application/javascript",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        _ => "application/octet-stream"
    };

    [GeneratedRegex(@"<link[^>]*?\bhref\s*=\s*[""']([^""']+\.css[^""']*)[""'][^>]*?>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkCssRegex();

    [GeneratedRegex(@"<img[^>]*?\bsrc\s*=\s*[""']([^""']+)[""'][^>]*?>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgSrcRegex();

    [GeneratedRegex(@"<script[^>]*?\bsrc\s*=\s*[""']([^""']+)[""'][^>]*?>[\s\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptSrcRegex();

    [GeneratedRegex(@"url\([""']?([^""'()]+)[""']?\)", RegexOptions.IgnoreCase)]
    private static partial Regex CssUrlRegex();
}
