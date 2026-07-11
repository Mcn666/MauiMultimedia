using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MauiMultimedia.Core.Abstractions;

namespace MauiMultimedia.Shell.Services;

/// <summary>
/// 轻量本地 HTTP 文件服务，支持 Range 请求。
/// 用于视频流式加载——浏览器通过 Range 头请求数据块，实现拖动进度条和流式播放。
/// </summary>
public sealed class FileServerService : IFileServerService, IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();

    // token → 规范化后的绝对路径。只有宿主（C#）能写入，
    // WebView 内的 JS 拿不到令牌就无法访问任意文件。
    private readonly ConcurrentDictionary<string, string> _tokenMap = new();
    private readonly ConcurrentDictionary<string, string> _dirTokenMap = new();

    public int Port { get; private set; }
    public bool IsRunning => Volatile.Read(ref _listener) != null;
    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public void Start()
    {
        lock (_lock)
        {
            if (_listener != null) return;

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _cts = new CancellationTokenSource();

            Debug.WriteLine($"[FileServer] Started on {BaseUrl}");
            _ = AcceptLoopAsync(_cts.Token);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener = null;
            Debug.WriteLine("[FileServer] Stopped");
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _tokenMap.Clear();
    }

    // ═══════════ 令牌注册（宿主侧 API） ═══════════

    /// <inheritdoc/>
    public string RegisterFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("文件路径不能为空", nameof(filePath));
        if (!File.Exists(filePath))
            throw new ArgumentException($"文件不存在：{filePath}", nameof(filePath));

        // 规范化：解析相对段与 '..'，得到真实绝对路径，防止注册时混入穿越序列。
        var canonical = Path.GetFullPath(filePath);
        if (!File.Exists(canonical))
            throw new ArgumentException($"文件不存在：{filePath}", nameof(filePath));

        var token = Guid.NewGuid().ToString("N");
        _tokenMap[token] = canonical;
        Debug.WriteLine($"[FileServer] Registered token for {canonical}");
        return token;
    }

    /// <inheritdoc/>
    public void UnregisterFile(string token)
    {
        if (!string.IsNullOrEmpty(token))
            _tokenMap.TryRemove(token, out _);
    }

    /// <inheritdoc/>
    public string RegisterDirectory(string dirPath)
    {
        if (string.IsNullOrWhiteSpace(dirPath))
            throw new ArgumentException("路径不能为空", nameof(dirPath));
        var canonical = Path.GetFullPath(dirPath);
        if (!Directory.Exists(canonical))
            throw new ArgumentException($"目录不存在：{dirPath}", nameof(dirPath));
        var token = Guid.NewGuid().ToString("N");
        _dirTokenMap[token] = canonical;
        Debug.WriteLine($"[FileServer] Registered dir token for {canonical}");
        return token;
    }

    /// <inheritdoc/>
    public void UnregisterDirectory(string token)
    {
        if (!string.IsNullOrEmpty(token))
            _dirTokenMap.TryRemove(token, out _);
    }

    // ═══════════ 接受循环 ═══════════

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileServer] Accept error: {ex.Message}");
            }
        }
    }

    // ═══════════ 请求处理 ═══════════

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                var (method, queryPath, headers) = await ReadHttpRequestAsync(stream, ct);
                if (string.IsNullOrEmpty(method)) return;

                // ── 目录服务 /dir/{token}/relative/path ──
                // 用于 Three.js/MMDLoader 等需要按相对路径加载附加工件的场景。
                var dm = System.Text.RegularExpressions.Regex.Match(
                    queryPath, @"^/dir/([^/]+)(/.*)?(\?.*)?$");
                if (dm.Success && method == "GET")
                {
                    var dirToken = dm.Groups[1].Value;
                    var relPath = dm.Groups[2].Value;
                    if (string.IsNullOrEmpty(relPath))
                    { await WriteErrorAsync(stream, 400, "Bad Request", ct); return; }

                    relPath = Uri.UnescapeDataString(relPath.TrimStart('/'));
                    relPath = relPath.Replace('\\', '/');
                    var ci = relPath.IndexOf(':');
                    if (ci > 0 && ci < 4) relPath = relPath.Substring(ci + 1);
                    relPath = relPath.TrimStart('/');

                    _dirTokenMap.TryGetValue(dirToken, out var dir);
                    if (string.IsNullOrEmpty(dir))
                    { await WriteErrorAsync(stream, 403, "Forbidden", ct); return; }

                    string fullPath;
                    try { fullPath = Path.GetFullPath(Path.Combine(dir, relPath)); }
                    catch { fullPath = ""; }

                    if (string.IsNullOrEmpty(fullPath) || !fullPath.StartsWith(dir + Path.DirectorySeparatorChar))
                    { await WriteErrorAsync(stream, 403, "Forbidden", ct); return; }

                    if (!File.Exists(fullPath))
                    {
                        var fb = Path.Combine(dir, Path.GetFileName(relPath));
                        if (Path.GetFileName(relPath) != relPath && File.Exists(fb))
                            fullPath = fb;
                        else
                        { await WriteErrorAsync(stream, 404, "Not Found", ct); return; }
                    }

                    var rh = GetHeaderValue(headers, "Range");
                    var (hr, rs, re) = ParseRangeHeader(rh);
                    await ServeFileAsync(stream, fullPath, hr, rs, re, ct);
                    return;
                }

                // 解析访问令牌
                var token = ParseQueryParam(queryPath, "token");

                // CORS 预检
                if (method == "OPTIONS")
                {
                    await WriteCorsResponseAsync(stream, ct);
                    return;
                }

                if (method != "GET" || string.IsNullOrEmpty(token))
                {
                    await WriteErrorAsync(stream, 400, "Bad Request", ct);
                    return;
                }

                // 只接受已注册的令牌；未知令牌一律拒绝，杜绝通过 ?path= 读取任意文件。
                if (!_tokenMap.TryGetValue(token, out var registeredPath) ||
                    string.IsNullOrEmpty(registeredPath))
                {
                    await WriteErrorAsync(stream, 403, "Forbidden", ct);
                    return;
                }

                // 纵深防御：再次规范化并确认文件仍存在，防止符号链接/挂载点绕过。
                string filePath;
                try { filePath = Path.GetFullPath(registeredPath); }
                catch
                {
                    await WriteErrorAsync(stream, 403, "Forbidden", ct);
                    return;
                }

                if (!File.Exists(filePath))
                {
                    await WriteErrorAsync(stream, 404, "Not Found", ct);
                    return;
                }

                // 解析 Range 头
                var rangeHeader = GetHeaderValue(headers, "Range");
                var (hasRange, rangeStart, rangeEnd) = ParseRangeHeader(rangeHeader);

                await ServeFileAsync(stream, filePath, hasRange, rangeStart, rangeEnd, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileServer] Handle error: {ex.Message}");
            }
        }
    }

    // ═══════════ HTTP 读取 ═══════════

    private static async Task<(string method, string path, List<string> headers)>
        ReadHttpRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var headerBytes = new List<byte>(512);
        var buf = new byte[1];

        // 逐字节读取直到 \r\n\r\n
        while (true)
        {
            var read = await stream.ReadAsync(buf.AsMemory(0, 1), ct);
            if (read == 0) return ("", "", new());
            headerBytes.Add(buf[0]);

            var n = headerBytes.Count;
            if (n >= 4 &&
                headerBytes[n - 4] == '\r' &&
                headerBytes[n - 3] == '\n' &&
                headerBytes[n - 2] == '\r' &&
                headerBytes[n - 1] == '\n')
                break;

            // 安全限制：拒绝超长头部
            if (n > 8192)
                return ("", "", new());
        }

        var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
        var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return ("", "", new());

        var parts = lines[0].Split(' ');
        var method = parts.Length > 0 ? parts[0].ToUpperInvariant() : "";
        var path = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";

        var headers = new List<string>(lines.Length - 1);
        for (int i = 1; i < lines.Length; i++)
            headers.Add(lines[i]);

        return (method, path, headers);
    }

    private static string ParseQueryParam(string uri, string key)
    {
        var qIndex = uri.IndexOf('?');
        if (qIndex < 0) return "";

        var query = uri[(qIndex + 1)..];
        foreach (var pair in query.Split('&'))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0] == key)
                return Uri.UnescapeDataString(kv[1]);
        }
        return "";
    }

    private static string GetHeaderValue(List<string> headers, string name)
    {
        foreach (var h in headers)
        {
            if (h.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                var colon = h.IndexOf(':');
                if (colon > 0) return h[(colon + 1)..].Trim();
            }
        }
        return "";
    }

    private static (bool hasRange, long start, long? end) ParseRangeHeader(string rangeHeader)
    {
        if (string.IsNullOrEmpty(rangeHeader) || !rangeHeader.StartsWith("bytes="))
            return (false, 0, null);

        var rangeStr = rangeHeader[6..];
        var parts = rangeStr.Split('-');
        if (parts.Length == 0) return (false, 0, null);

        long start = 0;
        long? end = null;

        if (!long.TryParse(parts[0], out start))
            return (false, 0, null);

        if (parts.Length > 1 && long.TryParse(parts[1], out var e))
            end = e;

        return (true, start, end);
    }

    // ═══════════ 文件服务 ═══════════

    private static async Task ServeFileAsync(
        NetworkStream stream, string filePath,
        bool hasRange, long rangeStart, long? rangeEnd,
        CancellationToken ct)
    {
        var fileInfo = new FileInfo(filePath);
        var fileLength = fileInfo.Length;
        var mimeType = GetMimeType(filePath);

        if (hasRange)
        {
            var end = rangeEnd ?? (fileLength - 1);
            end = Math.Min(end, fileLength - 1);

            if (rangeStart >= fileLength || rangeStart > end)
            {
                await WriteErrorAsync(stream, 416,
                    "Range Not Satisfiable", ct);
                return;
            }

            var contentLength = end - rangeStart + 1;
            var header = BuildHeader(206, "Partial Content", new()
            {
                ["Content-Type"] = mimeType,
                ["Content-Range"] = $"bytes {rangeStart}-{end}/{fileLength}",
                ["Content-Length"] = contentLength.ToString(),
                ["Accept-Ranges"] = "bytes",
                ["Access-Control-Allow-Origin"] = "*",
                ["Connection"] = "close",
            });

            await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);

            using var fs = new FileStream(filePath, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(rangeStart, SeekOrigin.Begin);
            // 只发送声明的 contentLength 字节，而非从 rangeStart 到文件末尾的全部内容。
            // 否则浏览器拖拽进度条 / 大视频会重复拉取整段尾部，浪费带宽且可能出错。
            await CopyExactAsync(fs, stream, contentLength, ct);
        }
        else
        {
            var header = BuildHeader(200, "OK", new()
            {
                ["Content-Type"] = mimeType,
                ["Content-Length"] = fileLength.ToString(),
                ["Accept-Ranges"] = "bytes",
                ["Access-Control-Allow-Origin"] = "*",
                ["Connection"] = "close",
            });

            await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);

            using var fs = new FileStream(filePath, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite);
            await fs.CopyToAsync(stream, 81920, ct);
        }
    }

    // 精确复制 count 字节，避免 CopyToAsync 把流剩余部分一并写出
    private static async Task CopyExactAsync(
        Stream source, Stream destination, long count, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long remaining = count;
        while (remaining > 0)
        {
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = await source.ReadAsync(buffer.AsMemory(0, toRead), ct);
            if (read == 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            remaining -= read;
        }
    }

    // ═══════════ 响应工具 ═══════════

    private static string BuildHeader(int statusCode, string statusText,
        Dictionary<string, string> headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {statusCode} {statusText}\r\n");
        foreach (var (k, v) in headers)
            sb.Append($"{k}: {v}\r\n");
        sb.Append("\r\n");
        return sb.ToString();
    }

    private static async Task WriteCorsResponseAsync(
        NetworkStream stream, CancellationToken ct)
    {
        var header = BuildHeader(200, "OK", new()
        {
            ["Access-Control-Allow-Origin"] = "*",
            ["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS",
            ["Access-Control-Allow-Headers"] = "Range",
            ["Access-Control-Max-Age"] = "86400",
            ["Content-Length"] = "0",
        });
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
    }

    private static async Task WriteErrorAsync(
        NetworkStream stream, int statusCode, string msg,
        CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(msg);
        var header = BuildHeader(statusCode, msg, new()
        {
            ["Content-Type"] = "text/plain; charset=utf-8",
            ["Content-Length"] = body.Length.ToString(),
            ["Access-Control-Allow-Origin"] = "*",
            ["Connection"] = "close",
        });

        var response = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(response, ct);
        if (body.Length > 0)
            await stream.WriteAsync(body, ct);
    }

    // ═══════════ MIME ═══════════

    private static string GetMimeType(string filePath)
    {
        return Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant() switch
        {
            // ── Image (served directly to the WebView for large-image viewing) ──
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "bmp" => "image/bmp",
            "ico" => "image/x-icon",
            "avif" => "image/avif",
            "svg" => "image/svg+xml",
            "dds" => "image/x-dds",

            // ── Web / static assets (MHTML inline resources, fonts, etc.) ──
            // Browsers enforce strict MIME checking on stylesheets/scripts, so
            // these MUST be served with the correct Content-Type or they are
            // silently rejected (e.g. layout breaks for CSS served as octet-stream).
            "css" => "text/css",
            "js" or "mjs" => "text/javascript",
            "json" => "application/json",
            "html" or "htm" => "text/html",
            "txt" => "text/plain",
            "xml" => "application/xml",
            "woff" => "font/woff",
            "woff2" => "font/woff2",
            "ttf" => "font/ttf",
            "otf" => "font/otf",
            "eot" => "application/vnd.ms-fontobject",

            // TIFF is not natively rendered by browsers; octet-stream forces a
            // download/sniff fallback rather than a broken inline render.
            "tiff" or "tif" => "application/octet-stream",

            // ── Video (Range-streamed playback) ──
            "mp4" or "m4v" => "video/mp4",
            "webm" => "video/webm",
            "mkv" => "video/x-matroska",
            "mov" => "video/quicktime",
            "avi" => "video/x-msvideo",
            "wmv" => "video/x-ms-wmv",
            "flv" => "video/x-flv",
            "3gp" => "video/3gpp",
            "ogv" => "video/ogg",
            "mpg" or "mpeg" => "video/mpeg",
            "ts" or "mts" => "video/mp2t",
            _ => "application/octet-stream"
        };
    }
}
