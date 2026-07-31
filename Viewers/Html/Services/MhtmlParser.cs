using System.Text;
using System.Text.RegularExpressions;

namespace MauiMultimedia.Viewers.Html.Services;

/// <summary>
/// MHTML (MIME HTML) 解析器。
/// 返回 htmlBody + 资源列表，由调用方创建 Blob URL。
/// </summary>
public static partial class MhtmlParser
{
    private const int MaxPartBytes = 20_000_000;
    private const int MaxResources = 500;

    public static ParseResult Parse(string filePath)
    {
        var text = File.ReadAllText(filePath, Encoding.UTF8);
        return ParseText(text);
    }

    /// <summary>从字节数组解析 MHTML 内容</summary>
    public static ParseResult Parse(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);
        return ParseText(text);
    }

    private static ParseResult ParseText(string text)
    {
        var boundary = DetectBoundary(text);
        if (boundary == null)
            throw new InvalidDataException("无法找到 MIME boundary");

        var marker = $"--{boundary}";
        var endMarker = $"--{boundary}--";
        var parts = new List<RawPart>();

        var boundaryLines = FindBoundaryLines(text, marker, endMarker);
        if (boundaryLines == null)
            throw new InvalidDataException("未找到 boundary 起始行");

        for (int bi = 0; bi < boundaryLines.Length - 1; bi++)
        {
            int ps = boundaryLines[bi] + 1;
            int pe = boundaryLines[bi + 1];
            int hdrStart = SkipBlankLines(text, ps, pe);
            if (hdrStart >= pe) continue;

            var cur = text.AsSpan(hdrStart, Math.Min(100, pe - hdrStart)).TrimStart();
            if (cur.StartsWith(endMarker.AsSpan())) break;
            if (cur.StartsWith(marker.AsSpan())) continue;

            var ct = ""; var cl = ""; var cte = "";
            int hdrEnd = hdrStart;
            while (hdrEnd < pe)
            {
                var line = ReadLine(text, ref hdrEnd);
                if (line.Length == 0) break;
                while (hdrEnd < pe)
                {
                    var nxt = PeekLine(text, hdrEnd);
                    if (nxt.Length > 0 && (nxt[0] == ' ' || nxt[0] == '\t'))
                    {
                        // 续行（折叠头）：ReadLine 读的就是当前 tab/空格行，
                        // 不能先手动跳过——否则会多跳一行，把空行/正文吞进头部
                        line += " " + ReadLine(text, ref hdrEnd).TrimStart();
                    }
                    else break;
                }
                var colon = line.IndexOf(':');
                if (colon > 0)
                {
                    var key = line[..colon].Trim().ToUpperInvariant();
                    var val = line[(colon + 1)..].Trim();
                    if (key == "CONTENT-TYPE") ct = val;
                    else if (key == "CONTENT-LOCATION") cl = val;
                    else if (key == "CONTENT-TRANSFER-ENCODING") cte = val;
                }
            }

            bool isHtml = ct.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
            bool needBody = isHtml || !string.IsNullOrEmpty(cl);
            string? bodyStr = null;
            if (needBody && pe - hdrEnd > 0)
            {
                int bodyLen = pe - hdrEnd;
                if (isHtml || bodyLen <= MaxPartBytes)
                    bodyStr = text.Substring(hdrEnd, bodyLen);
            }
            parts.Add(new RawPart(ct, cl, cte, bodyStr));
        }

        // 找 HTML
        string? htmlBody = null;
        foreach (var p in parts)
            if (p.ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                { htmlBody = DecodePart(p); if (htmlBody != null) break; }
        if (htmlBody == null)
            foreach (var p in parts)
                if (p.ContentLocation.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ||
                    p.ContentLocation.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    { htmlBody = DecodePart(p); if (htmlBody != null) break; }
        if (htmlBody == null)
            foreach (var p in parts)
            {
                var b = DecodePart(p); if (b == null) continue;
                var t = b.AsSpan().TrimStart();
                if (t.StartsWith("<html".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                    t.StartsWith("<!doctype".AsSpan(), StringComparison.OrdinalIgnoreCase))
                { htmlBody = b; break; }
            }
        if (htmlBody == null)
            throw new InvalidDataException("未找到 HTML 内容");

        // 收集资源 — 返回原始字节数组，由调用方创建 Blob URL
        var resources = new List<ResourceBlob>();
        var resourceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int count = 0;

        foreach (var p in parts)
        {
            if (string.IsNullOrEmpty(p.ContentLocation)) continue;
            if (count >= MaxResources) break;

            var mime = ExtractMime(p.ContentType);
            var bytes = DecodePartBytes(p);
            if (bytes == null) continue;

            var token = $"__BLOB_{count}__";
            resources.Add(new ResourceBlob(p.ContentLocation, bytes, mime));
            resourceMap[p.ContentLocation] = token;
            var fn = Path.GetFileName(p.ContentLocation);
            if (!string.IsNullOrEmpty(fn)) resourceMap[fn] = token;
            count++;
        }

        if (resourceMap.Count > 0)
            htmlBody = RewriteReferences(htmlBody, resourceMap);

        return new ParseResult(htmlBody, resources);
    }

    // ── 辅助方法 ──────────────────────────────────────

    private static string? DetectBoundary(string text)
    {
        var headerText = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            if (line.Trim().Length == 0) break;
            headerText.Append(line.TrimEnd('\r'));
        }
        var m = BoundaryRegex().Match(headerText.ToString());
        if (m.Success)
        {
            var b = m.Groups[1].Value.Trim('"');
            if (!string.IsNullOrEmpty(b)) return b;
        }
        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length > 2 && t.StartsWith("--") && !t.StartsWith("---"))
            {
                var b = t[2..].TrimEnd('\r');
                if (!string.IsNullOrEmpty(b) && !b.EndsWith("--")) return b;
            }
        }
        return null;
    }

    private static int[]? FindBoundaryLines(string text, string marker, string endMarker)
    {
        var pos = new List<int>();
        int p = 0;
        while (true)
        {
            var nl = text.IndexOf('\n', p);
            if (nl < 0) break;
            int start = p; p = nl + 1;
            int cs = SkipLeadingWhitespace(text, start, p);
            if (cs >= p) continue;
            var span = text.AsSpan(cs, p - cs - 1).TrimEnd('\r');
            if (span.StartsWith(endMarker.AsSpan())) { pos.Add(start); break; }
            if (span.StartsWith(marker.AsSpan())) pos.Add(start);
        }
        return pos.Count > 0 ? pos.ToArray() : null;
    }

    private static int SkipLeadingWhitespace(string text, int start, int end)
    {
        int p = start;
        while (p < end && (text[p] == ' ' || text[p] == '\t' || text[p] == '\r')) p++;
        return p;
    }

    private static int SkipBlankLines(string text, int start, int end)
    {
        int p = start;
        while (p < end)
        {
            var nl = text.IndexOf('\n', p);
            if (nl < 0 || nl >= end) return end;
            if (text.AsSpan(p, nl - p).TrimEnd('\r').Length > 0) return p;
            p = nl + 1;
        }
        return p;
    }

    private static string ReadLine(string text, ref int pos)
    {
        int start = pos;
        var nl = text.IndexOf('\n', pos);
        if (nl < 0) { pos = text.Length; return text[start..].TrimEnd('\r'); }
        pos = nl + 1;
        return text[start..nl].TrimEnd('\r');
    }

    private static string PeekLine(string text, int pos)
    {
        var nl = text.IndexOf('\n', pos);
        if (nl < 0) return text[pos..].TrimEnd('\r');
        return text[pos..nl].TrimEnd('\r');
    }

    // ── 解码 ──────────────────────────────────────────

    private static string? DecodePart(RawPart p)
    {
        if (p.Body == null) return null;
        if (p.ContentTransferEncoding.Equals("base64", StringComparison.OrdinalIgnoreCase))
        {
            var clean = p.Body.Replace("\n", "").Replace("\r", "").Replace(" ", "");
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(clean)); }
            catch { return null; }
        }
        if (p.ContentTransferEncoding.Equals("quoted-printable", StringComparison.OrdinalIgnoreCase))
            return DecodeQp(p.Body, GetEncoding(p.ContentType));
        return p.Body;
    }

    private static string DecodeQp(string raw, Encoding enc)
    {
        var bytes = new List<byte>(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '=' && i + 2 < raw.Length && raw[i + 1] == '\r' && raw[i + 2] == '\n')
            { i += 2; continue; }
            if (raw[i] == '=' && i + 1 < raw.Length && raw[i + 1] == '\n')
            { i += 1; continue; }
            if (raw[i] == '=' && i + 2 < raw.Length)
            {
                int hi = HexVal(raw[i + 1]), lo = HexVal(raw[i + 2]);
                if (hi >= 0 && lo >= 0) { bytes.Add((byte)((hi << 4) | lo)); i += 2; continue; }
            }
            if (raw[i] != '\r') bytes.Add((byte)raw[i]);
        }
        return enc.GetString(bytes.ToArray());
    }

    private static int HexVal(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10, _ => -1
    };

    private static Encoding GetEncoding(string ct)
    {
        var m = CharsetRegex().Match(ct);
        if (m.Success) { try { return Encoding.GetEncoding(m.Groups[1].Value); } catch { } }
        return Encoding.UTF8;
    }

    private static string ExtractMime(string ct)
    {
        int s = ct.IndexOf(';');
        return s > 0 ? ct[..s].Trim() : ct.Trim();
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    private static string RewriteReferences(string html, Dictionary<string, string> res)
    {
        foreach (var (loc, val) in res)
        {
            var e = Regex.Escape(loc);
            html = Regex.Replace(html, $@"src\s*=\s*[""']{e}[""']", $"src=\"{val}\"", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, $@"href\s*=\s*[""']{e}[""']", $"href=\"{val}\"", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, $@"url\([""']?{e}[""']?\)", $"url({val})", RegexOptions.IgnoreCase);
        }
        return html;
    }

    /// <summary>
    /// 解析 MIME part 的原始字节数据（用于创建 Blob）
    /// </summary>
    private static byte[]? DecodePartBytes(RawPart p)
    {
        if (p.Body == null) return null;
        if (p.ContentTransferEncoding.Equals("base64", StringComparison.OrdinalIgnoreCase))
        {
            var clean = p.Body.Replace("\n", "").Replace("\r", "").Replace(" ", "");
            try { return Convert.FromBase64String(clean); }
            catch { return null; }
        }
        if (p.ContentTransferEncoding.Equals("quoted-printable", StringComparison.OrdinalIgnoreCase))
        {
            var decoded = DecodeQp(p.Body, GetEncoding(p.ContentType));
            return Encoding.UTF8.GetBytes(decoded);
        }
        return Encoding.UTF8.GetBytes(p.Body);
    }

    private record RawPart(string ContentType, string ContentLocation, string ContentTransferEncoding, string? Body);

    /// <summary>
    /// MHTML 解析结果：HTML 正文 + 资源引用列表
    /// </summary>
    public record ParseResult(string HtmlBody, List<ResourceBlob> Resources);

    /// <summary>
    /// 单个资源的原始数据
    /// </summary>
    public record ResourceBlob(string Location, byte[] Data, string MimeType);

    [GeneratedRegex(@"boundary\s*=\s*""?([^\s"";]+)""?", RegexOptions.IgnoreCase)]
    private static partial Regex BoundaryRegex();

    [GeneratedRegex(@"charset\s*=\s*""?([^""\s;]+)""?", RegexOptions.IgnoreCase)]
    private static partial Regex CharsetRegex();
}
