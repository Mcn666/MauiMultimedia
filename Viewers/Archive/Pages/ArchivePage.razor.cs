using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Threading;
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
    [Inject] private IEnumerable<IItemPresenter> Presenters { get; set; } = null!;

    // 嵌套档案检测（档案内的条目本身是否可再次打开）；与查看器注册共用常量
    private static readonly HashSet<string> Arc = ArchiveConstants.Exts;

    private string filePath = "", fileName = "";
    // 改为实例字段：static 会导致跨实例共享密码（打开档案 A 的密码被复用到加密档案 B），
    // 且密码常驻进程内存可被转储提取。导航返回/Dispose 时清空。
    private string? archivePassword;
    private bool isLoading = true;
    private string? errorMessage;
    private string? toast;
    private bool showPasswordPrompt;
    private List<TreeItem> nodes = new();
    private List<Entry> items = new();
    private HashSet<string> open = new(StringComparer.OrdinalIgnoreCase);
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
            // 读取整包字节用于扫描条目；扫描完成后即离开作用域，由 GC 回收，
            // 避免数百 MB 的整包缓冲长期驻留内存（老旧设备易触发 GC 卡顿）。
            var archiveBytes = await File.ReadAllBytesAsync(p);
            items = await Task.Run(() => Scan(new MemoryStream(archiveBytes), archivePassword));
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
                b.AddContent(6, GetIconForFile(n.Label));
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
                var tmp = FileSystem.GetScratchDirectory("Archive");
                var outPath = PathSandbox.EnsureWithin(tmp, Path.Combine(tmp, n.Full));
                var dir = Path.GetDirectoryName(outPath);
                if (dir != null) Directory.CreateDirectory(dir);

                // 直接读取文件数据
                var archiveStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                ExtractEntry(archiveStream, n.Full, outPath, archivePassword);
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
    private List<Entry> ReadArchiveEntries(Stream stream) => Scan(stream, archivePassword);

    /// <summary>从档案文件路径读取条目列表（不把整包字节驻留内存）</summary>
    private List<Entry> ReadArchiveEntriesFromPath(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Scan(fs, archivePassword);
    }

    private void Open(TreeItem n)
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

            var tmp = FileSystem.GetScratchDirectory("Archive");

            // 仅提取当前点击的文件，立即导航（直接从档案文件读取，不再把整包字节驻留内存）
            var srcPath = n.Source ?? filePath;
            var outPath = PathSandbox.EnsureWithin(tmp, Path.Combine(tmp, n.Full));
            var dir = Path.GetDirectoryName(outPath);
            if (dir != null) Directory.CreateDirectory(dir);
            using (var fs = new FileStream(srcPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                ExtractEntry(fs, n.Full, outPath, archivePassword);

            // 构建同级文件路径列表（仅路径，不提取，加快响应）
            List<string> fileList;
            List<Entry> siblingEntries;
            if (n.Source != null)
            {
                var all = ReadArchiveEntriesFromPath(srcPath);
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

            // 后台以低优先级解压同级文件：避免与新打开的图片/视频查看器抢占 CPU 与磁盘，
            // 否则在老旧设备上会出现导航后翻页明显卡顿（解压仍会完成，翻页最终可用）。
            _ = ExtractBackgroundAsync(srcPath, tmp, siblingEntries);
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

    /// <summary>从指定档案中提取一个条目到目标路径（调用方负责确保目录已创建）</summary>
    private static void ExtractEntry(Stream archiveStream, string entryName, string outPath, string? password)
    {
        EnsureEncoding();
        var encoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        var opts = new ReaderOptions
        {
            Password = password,
            ArchiveEncoding = new SharpCompress.Common.ArchiveEncoding { Default = encoding }
        };
        using var archive = ArchiveFactory.OpenArchive(archiveStream, opts);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory) continue;
            if ((entry.Key ?? "") == entryName)
            {
                var dir = Path.GetDirectoryName(outPath);
                if (dir != null) Directory.CreateDirectory(dir);
                entry.WriteToFile(outPath, new ExtractionOptions { Overwrite = true });
                return;
            }
        }
    }

    /// <summary>
    /// 单次打开档案并遍历，提取所有命中的条目（O(n) 单次遍历）。
    /// 取代原先「每个条目重开整个档案」的做法，消除含大量条目归档的 O(n²) I/O 与卡顿。
    /// </summary>
    private static void ExtractEntries(Stream archiveStream, IEnumerable<(string entryName, string outPath)> wanted, string? password)
    {
        EnsureEncoding();
        var encoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        var opts = new ReaderOptions
        {
            Password = password,
            ArchiveEncoding = new SharpCompress.Common.ArchiveEncoding { Default = encoding }
        };
        var wantDict = wanted.ToDictionary(x => x.entryName, x => x.outPath, StringComparer.OrdinalIgnoreCase);
        using var archive = ArchiveFactory.OpenArchive(archiveStream, opts);
        var written = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory) continue;
            var key = entry.Key ?? "";
            if (wantDict.TryGetValue(key, out var outPath))
            {
                var dir = Path.GetDirectoryName(outPath);
                if (dir != null) Directory.CreateDirectory(dir);
                entry.WriteToFile(outPath, new ExtractionOptions { Overwrite = true });
                // 每写若干文件让出一次时间片，进一步降低对前台查看器的干扰
                if ((++written % 16) == 0) Thread.Sleep(0);
            }
        }
    }

    /// <summary>获取条目所在父目录路径（无尾部斜杠，根目录返回 ""）</summary>
    private static string GetParent(string full)
    {
        var idx = full.LastIndexOf('/');
        return idx > 0 ? full[..idx] : "";
    }

    /// <summary>后台提取同级文件，不阻塞导航到查看器（仅打开档案一次）</summary>
    private Task ExtractBackgroundAsync(string srcPath, string tmp, List<Entry> entries)
    {
        return Task.Run(() =>
        {
            try
            {
                // 降低工作线程优先级：解压同级文件不再与刚打开的图片/视频查看器抢占 CPU，
                // 导航后翻页在老旧设备上不再明显卡顿（解压仍会完成，翻页最终可用）。
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                // 直接从档案文件读取，避免再次把整包字节载入内存。
                var wanted = entries.Select(e => (e.Full, Path.Combine(tmp, e.Full)));
                using var s = new FileStream(srcPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                ExtractEntries(s, wanted, archivePassword);
            }
            catch { }
        });
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
            // 仅删除当前档案对应的子目录，避免误删其他（嵌套/多）归档已提取的文件。
            var archiveSubDir = FileSystem.GetScratchDirectory("Archive");
            if (Directory.Exists(archiveSubDir))
                Directory.Delete(archiveSubDir, true);
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

    public void Dispose()
    {
        // 离开页面时清空密码，避免实例残留于内存且被后续实例复用。
        archivePassword = null;
    }

    private static string Sz(long b) { double d = b; if (d < 1024) return $"{d:F0} B"; d /= 1024; if (d < 1024) return $"{d:F1} KB"; return $"{d / 1024:F1} MB"; }
    private static string Dt(DateTime d) => d == DateTime.MinValue ? "" : d.ToString("yyyy/MM/dd HH:mm");
    private string GetIconForFile(string name)
    {
        var item = new FileSystemItem { Name = name, IsFolder = false };
        foreach (var p in Presenters)
        {
            if (p.CanHandle(item))
            {
                var icon = p.GetIcon(item);
                if (icon != null) return icon;
            }
        }
        return "\uD83D\uDCCE"; // 📎 default
    }
}
