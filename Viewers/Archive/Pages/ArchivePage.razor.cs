using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using MauiMultimedia.Core.Abstractions;
using System.Linq;
using System.Text;
using System.Globalization;
using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Viewers.Archive.Pages;

public partial class ArchivePage : ComponentBase, IDisposable
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IFileNavigationState NavState { get; set; } = null!;
    [Inject] private IMauiNavigation MauiNav { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private IEnumerable<IFileViewer> Viewers { get; set; } = null!;
    [Inject] private IFileSystemService FileSystem { get; set; } = null!;

    private static readonly HashSet<string> Img = new(StringComparer.OrdinalIgnoreCase)
    { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".svg" };
    private static readonly HashSet<string> Doc = new(StringComparer.OrdinalIgnoreCase)
    { ".txt", ".log", ".md", ".csv", ".xml", ".json", ".yaml", ".yml",
      ".cs", ".js", ".ts", ".html", ".htm", ".css", ".jsx", ".tsx",
      ".py", ".java", ".cpp", ".c", ".h", ".sql", ".sh", ".bat", ".ps1",
      ".ini", ".cfg", ".conf", ".csproj", ".sln", ".slnx" };
    private static readonly HashSet<string> Arc = new(StringComparer.OrdinalIgnoreCase)
    { ".zip", ".tar", ".gz", ".tgz", ".rar", ".7z", ".zst", ".xz", ".bz2" };

    private string filePath = "", fileName = "";
    private static string? archivePassword;
    private bool isLoading = true;
    private string? errorMessage;
    private string? toast;
    private bool showPasswordPrompt;
    private List<TreeItem> nodes = new();
    private List<Entry> items = new();
    private HashSet<string> open = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>加密的档案字节缓存，避免每次提取都重新读磁盘</summary>
    private byte[]? _archiveBytes;
    private HashSet<string> loaded = new(StringComparer.OrdinalIgnoreCase);

    private bool IsArchive(string name)
    {
        var e = Path.GetExtension(name);
        return Arc.Contains(e) || name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
    }

    private record Entry(string Full, string Name, long Size, DateTime Modified, bool IsDir);
    private class TreeItem
    {
        public string Label { get; set; }
        public string Full { get; set; }
        public bool Dir { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public int Level { get; set; }
        public List<TreeItem> Kids { get; set; } = new();
        public string? Source { get; set; }

        public TreeItem(string label, string full, bool dir, long size, DateTime modified, int level, List<TreeItem> kids, string? source = null)
        {
            Label = label; Full = full; Dir = dir; Size = size; Modified = modified; Level = level; Kids = kids; Source = source;
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await JS.InvokeVoidAsync("eval", "document.documentElement.style.overflowY='hidden'");
        await Load();
    }

    private async Task Load()
    {
        var q = Navigation.ToAbsoluteUri(Navigation.Uri).Query.TrimStart('?');
        var p = NavState.CurrentFilePath
            ?? q.Split('&').Select(s => s.Split('=', 2))
                .Where(kv => kv.Length == 2 && kv[0] == "path")
                .Select(kv => Uri.UnescapeDataString(kv[1]))
                .FirstOrDefault() ?? "";
        if (string.IsNullOrEmpty(p)) { errorMessage = "无路径"; isLoading = false; return; }
        filePath = p; fileName = Path.GetFileName(p);
        try
        {
            if (!File.Exists(p)) { errorMessage = "文件不存在"; return; }
            // 一次性读取字节并缓存，后续提取复用避免重复 I/O
            _archiveBytes = await File.ReadAllBytesAsync(p);
            items = await Task.Run(() => Scan(new MemoryStream(_archiveBytes), archivePassword));
            nodes = Build(items);
        }
        catch (Exception ex)
        {
            if (IsPasswordError(ex))
            {
                showPasswordPrompt = true;
                archivePassword = null;
                errorMessage = null;
            }
            else
                errorMessage = $"读取失败：{ex.Message}";
        }
        finally { isLoading = false; StateHasChanged(); }
    }

    private static List<Entry> Scan(Stream stream, string? password = null)
    {
        EnsureEncoding();
        var encoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        var opts = new ReaderOptions
        {
            Password = password,
            ArchiveEncoding = new SharpCompress.Common.ArchiveEncoding { Default = encoding }
        };
        using var archive = ArchiveFactory.OpenArchive(stream, opts);
        var result = new List<Entry>();
        foreach (var entry in archive.Entries)
        {
            var key = entry.Key ?? "";
            result.Add(new Entry(
                key.TrimEnd('/'),
                Path.GetFileName(key.TrimEnd('/')),
                entry.Size,
                entry.LastModifiedTime ?? DateTime.MinValue,
                entry.IsDirectory));
        }
        result.Sort((x, y) => MauiMultimedia.Core.Utils.NaturalSortComparer.Instance.Compare(x.Full, y.Full));
        return result;
    }

    private static bool _encRegistered;
    private static void EnsureEncoding()
    {
        if (_encRegistered) return;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _encRegistered = true;
    }

    private static List<TreeItem> Build(List<Entry> entries)
    {
        var root = new List<TreeItem>();
        var map = new Dictionary<string, List<TreeItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in entries)
        {
            var parts = r.Full.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var cur = "";
            for (int i = 0; i < parts.Length - 1; i++)
            {
                cur = string.IsNullOrEmpty(cur) ? parts[i] : cur + "/" + parts[i];
                if (map.ContainsKey(cur)) continue;
                var n = new TreeItem(parts[i], cur, true, 0, DateTime.MinValue, i, new());
                map[cur] = n.Kids;
                (i == 0 ? root : map[cur[..^(parts[i].Length + 1)]]).Add(n);
            }
            var parent = parts.Length == 1 ? root : map[string.Join("/", parts[..^1])];
            if (r.IsDir)
            {
                if (map.ContainsKey(r.Full)) continue;
                var n = new TreeItem(r.Name, r.Full, true, 0, r.Modified, parts.Length - 1, new());
                map[r.Full] = n.Kids;
                parent.Add(n);
            }
            else
            {
                var f = new TreeItem(r.Name, r.Full, false, r.Size, r.Modified, parts.Length - 1, new());
                parent.Add(f);
            }
        }
        Sort(root);
        return root;
    }

    private static void Sort(List<TreeItem> items)
    {
        items.Sort((a, b) => a.Dir != b.Dir ? (a.Dir ? -1 : 1) : MauiMultimedia.Core.Utils.NaturalSortComparer.Instance.Compare(a.Label, b.Label));
        foreach (var n in items) Sort(n.Kids);
    }

    /// <summary>重建树节点层级，统一增加偏移量</summary>
    private static void RemapLevels(List<TreeItem> items, int add)
    {
        foreach (var n in items)
        {
            n.Level = n.Level + add;
            RemapLevels(n.Kids, add);
        }
    }

    /// <summary>标记来自内层压缩包的文件来源路径</summary>
    private static void MarkSource(List<TreeItem> items, string archivePath)
    {
        foreach (var n in items)
        {
            if (!n.Dir) n.Source = archivePath;
            MarkSource(n.Kids, archivePath);
        }
    }

    private RenderFragment RenderTree(List<TreeItem> nodes) => b =>
    {
        foreach (var n in nodes)
        {
            var ok = open.Contains(n.Full);
            var pad = 36 + n.Level * 20;
            var isArc = !n.Dir && IsArchive(n.Label);

            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "item-row" + (isArc || n.Dir ? " folder" : " file"));
            b.AddAttribute(2, "style", $"padding-left:{pad}px");

            // 压缩文件或文件夹 → 可展开
            if (n.Dir || isArc)
            {
                b.AddAttribute(3, "onclick", EventCallback.Factory.Create(this, () => Tog(n)));
                b.OpenElement(4, "span"); b.AddAttribute(5, "class", "col-icon item-icon");
                b.AddContent(6, $"{(ok ? "▾" : "▸")}");
                b.CloseElement();
                b.OpenElement(7, "span"); b.AddAttribute(8, "class", "col-name item-name");
                b.AddContent(9, isArc ? $"📦 {n.Label}" : $"\uD83D\uDCC1 {n.Label}");
                b.CloseElement();
                b.OpenElement(10, "span"); b.AddAttribute(11, "class", "col-count item-count"); b.CloseElement();
                b.OpenElement(12, "span"); b.AddAttribute(13, "class", "col-date item-date"); b.CloseElement();
            }
            else
            {
                b.AddAttribute(3, "onclick", EventCallback.Factory.Create(this, () => Open(n)));
                b.OpenElement(4, "span"); b.AddAttribute(5, "class", "col-icon item-icon");
                b.AddContent(6, Icn(n.Label));
                b.CloseElement();
                b.OpenElement(7, "span"); b.AddAttribute(8, "class", "col-name item-name");
                b.AddContent(9, n.Label);
                b.CloseElement();
                b.OpenElement(10, "span"); b.AddAttribute(11, "class", "col-count item-count");
                b.AddContent(12, Sz(n.Size));
                b.CloseElement();
                b.OpenElement(13, "span"); b.AddAttribute(14, "class", "col-date item-date");
                b.AddContent(15, Dt(n.Modified));
                b.CloseElement();
            }
            b.CloseElement();

            if (ok) b.AddContent(16, RenderTree(n.Kids));
        }
    };

    // ═══ 展开/折叠（支持嵌套压缩包） ═══

    private async Task Tog(TreeItem n)
    {
        if (open.Contains(n.Full)) { open.Remove(n.Full); return; }

        // 压缩文件首次展开：提取并读取内部内容
        if (!n.Dir && IsArchive(n.Label) && !loaded.Contains(n.Full))
        {
            try
            {
                loaded.Add(n.Full);
                var tmp = Path.Combine(FileSystem.GetAppDataDirectory(), "MauiArchive",
                    Path.GetFileNameWithoutExtension(fileName));
                var outPath = Path.Combine(tmp, n.Full);
                var dir = Path.GetDirectoryName(outPath);
                if (dir != null) Directory.CreateDirectory(dir);

                // 直接读取文件数据
                var archiveStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                ExtractEntry(archiveStream, n.Full, outPath);
                // outPath 是刚提取出来的文件（在 AppData 中，未被锁定）
                using var innerStream = new FileStream(outPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var inner = ReadArchiveEntries(innerStream);
                var rawKids = Build(inner);
                RemapLevels(rawKids, n.Level + 1);
                MarkSource(rawKids, outPath);
                n.Kids = rawKids;
                // 标记来源档案路径，供 Open 提取时使用
                MarkSource(n.Kids, outPath);
            }
            catch (Exception ex)
            {
                errorMessage = $"内部压缩文件读取失败：{ex.Message}";
                StateHasChanged();
                return;
            }
        }
        open.Add(n.Full);
    }

    /// <summary>读取压缩文件的条目列表</summary>
    private static List<Entry> ReadArchiveEntries(Stream stream) => Scan(stream, archivePassword);

    private async Task Open(TreeItem n)
    {
        try
        {
            var fi = new FileSystemItem { Name = n.Label, FullPath = "", IsFolder = false, LastModified = DateTime.Now };
            var v = Viewers.FirstOrDefault(v => v.CanHandle(fi));
            if (v == null)
            {
                toast = "⚠️ 此文件类型无查看器支持";
                StateHasChanged();
                return;
            }

            var tmp = Path.Combine(FileSystem.GetAppDataDirectory(), "MauiArchive",
                Path.GetFileNameWithoutExtension(fileName));

            // 使用缓存的档案字节（避免重复读磁盘）
            var srcBytes = n.Source != null
                ? await File.ReadAllBytesAsync(n.Source)
                : _archiveBytes;
            if (srcBytes == null) { toast = "读取档案失败"; return; }

            // 只提取当前点击的文件，立即导航
            var outPath = Path.Combine(tmp, n.Full);
            var dir = Path.GetDirectoryName(outPath);
            if (dir != null) Directory.CreateDirectory(dir);
            using (var fs = new MemoryStream(srcBytes))
                ExtractEntry(fs, n.Full, outPath);

            // 构建同级文件路径列表（仅路径，不提取，加快响应）
            List<string> fileList;
            List<Entry> siblingEntries;
            if (n.Source != null)
            {
                var all = ReadArchiveEntries(new MemoryStream(srcBytes));
                siblingEntries = all;
                fileList = all.Select(e => Path.Combine(tmp, e.Full)).ToList();
            }
            else
            {
                var parent = GetParent(n.Full);
                siblingEntries = items.Where(e => GetParent(e.Full) == parent).ToList();
                fileList = siblingEntries.Select(e => Path.Combine(tmp, e.Full)).ToList();
            }
            fileList.Sort(MauiMultimedia.Core.Utils.NaturalSortComparer.Instance);
            NavState.CurrentDirectoryFiles = fileList;

            // 立即导航
            fi.FullPath = outPath;
            NavState.CurrentFilePath = outPath;
            NavState.ReturnUrl = Navigation.ToAbsoluteUri(Navigation.Uri).PathAndQuery;
            _ = MauiNav.NavigateToViewerAsync(v, fi);

            // 后台继续提取同级文件（用户已在查看器中，不影响体验）
            _ = ExtractBackgroundAsync(srcBytes, tmp, siblingEntries);
        }
        catch (Exception ex)
        {
            if (IsPasswordError(ex))
            {
                showPasswordPrompt = true;
                archivePassword = null;
                toast = null;
                errorMessage = null;
            }
            else
                toast = $"提取失败：{ex.Message}";
            StateHasChanged();
        }
    }

    /// <summary>从指定档案中提取一个条目到目标路径</summary>
    private static void ExtractEntry(Stream archiveStream, string entryName, string outPath)
    {
        EnsureEncoding();
        var encoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        var opts = new ReaderOptions
        {
            Password = archivePassword,
            ArchiveEncoding = new SharpCompress.Common.ArchiveEncoding { Default = encoding }
        };
        using var archive = ArchiveFactory.OpenArchive(archiveStream, opts);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory) continue;
            if ((entry.Key ?? "") == entryName)
            {
                entry.WriteToFile(outPath, new ExtractionOptions { Overwrite = true });
                return;
            }
        }
    }

    /// <summary>获取条目所在父目录路径（无尾部斜杠，根目录返回 ""）</summary>
    private static string GetParent(string full)
    {
        var idx = full.LastIndexOf('/');
        return idx > 0 ? full[..idx] : "";
    }

    /// <summary>后台提取同级文件，不阻塞导航到查看器</summary>
    private async Task ExtractBackgroundAsync(byte[] srcBytes, string tmp, List<Entry> entries)
    {
        await Task.Yield();
        foreach (var e in entries)
        {
            try
            {
                var p = Path.Combine(tmp, e.Full);
                if (File.Exists(p)) continue;
                var d = Path.GetDirectoryName(p);
                if (d != null) Directory.CreateDirectory(d);
                using var s = new MemoryStream(srcBytes);
                ExtractEntry(s, e.Full, p);
            }
            catch { }
        }
    }

    private void CancelPassword()
    {
        showPasswordPrompt = false;
        if (items.Count == 0) errorMessage = "已取消密码输入";
    }

    private async Task DoPassword()
    {
        archivePassword = await JS.InvokeAsync<string>("eval",
            "document.getElementById('_pw').value");
        showPasswordPrompt = false;
        isLoading = true;
        StateHasChanged();
        await Load();
    }

    private void GoBack()
    {
        try
        {
            var cacheDir = Path.Combine(FileSystem.GetAppDataDirectory(), "MauiArchive");
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, true);
        }
        catch { }
        _ = MauiNav.GoBackAsync();
    }

    private static bool IsPasswordError(Exception ex)
    {
        var msg = ex.Message;
        return msg.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("encrypted", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() { }

    private static string Sz(long b) { double d = b; if (d < 1024) return $"{d:F0} B"; d /= 1024; if (d < 1024) return $"{d:F1} KB"; return $"{d / 1024:F1} MB"; }
    private static string Dt(DateTime d) => d == DateTime.MinValue ? "" : d.ToString("yyyy/MM/dd HH:mm");
    private static string Icn(string n) { var e = Path.GetExtension(n); if (Img.Contains(e)) return "🖼️"; if (Doc.Contains(e)) return "📄"; if (Arc.Contains(e) || n.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return "📦"; return "📎"; }
}
