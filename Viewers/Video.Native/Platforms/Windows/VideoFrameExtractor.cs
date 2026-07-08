using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MauiMultimedia.Viewers.Video;

namespace MauiMultimedia.Viewers.Video.Platforms.Windows;

/// <summary>
/// Windows 视频首帧提取：IShellItemImageFactory（Shell COM），即资源管理器取缩略图的同一机制。
/// 关键点：它走调用者令牌（与 System.IO.File 同一访问模型），不受 Windows.Storage broker 的
/// AppContainer 限制，因此文件管理器浏览到的任意路径都能取到帧，不依赖 broadFileSystemAccess。
/// 作为 Video 查看器的一部分自包含，不侵入 Shell。
/// 实现 <see cref="IVideoFrameExtractor"/>，由 Video RCL 在运行时自动发现并注册（无需 ModuleInitializer）。
/// </summary>
public class VideoFrameExtractor : IVideoFrameExtractor
{
    private const int ThumbSize = 256;

    public Task<byte[]?> TryExtractAsync(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return Task.FromResult<byte[]?>(null);

        // 在后台线程取帧，避免阻塞 Blazor 调度线程
        return Task.Run(() =>
        {
            try
            {
                var nativePath = path.Replace('/', '\\');
                var iid = ShellPInvoke.IID_IShellItem;
                var hr = ShellPInvoke.SHCreateItemFromParsingName(
                    nativePath, IntPtr.Zero, ref iid, out var pShellItem);
                if (hr != 0 || pShellItem == IntPtr.Zero)
                {
                    Debug.WriteLine($"[VideoSnap:Win] SHCreateItemFromParsingName 失败 hr=0x{hr:X8} path={nativePath}");
                    return (byte[]?)null;
                }

                try
                {
                    var factory = (IShellItemImageFactory)Marshal.GetTypedObjectForIUnknown(
                        pShellItem, typeof(IShellItemImageFactory));
                    var size = new SIZE { cx = ThumbSize, cy = ThumbSize };
                    // THUMBNAILONLY：仅缩略图（视频的代表帧）；RESIZETOFIT：缩放到请求尺寸。
                    var hresult = factory.GetImage(size,
                        SIIGBF.SIIGBF_THUMBNAILONLY | SIIGBF.SIIGBF_RESIZETOFIT, out var hBitmap);
                    if (hresult != 0 || hBitmap == IntPtr.Zero)
                    {
                        Debug.WriteLine($"[VideoSnap:Win] GetImage 失败/空 hr=0x{hresult:X8} path={nativePath}");
                        return (byte[]?)null;
                    }

                    try
                    {
                        using var bmp = System.Drawing.Image.FromHbitmap(hBitmap);
                        using var ms = new MemoryStream();
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                        var bytes = ms.ToArray();
                        Debug.WriteLine($"[VideoSnap:Win] 取帧成功 {bytes.Length} 字节 path={nativePath}");
                        return bytes;
                    }
                    finally
                    {
                        ShellPInvoke.DeleteObject(hBitmap);
                    }
                }
                finally
                {
                    Marshal.Release(pShellItem);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VideoSnap:Win] 取帧异常 {ex.GetType().Name}: {ex.Message} path={path}");
                return (byte[]?)null;
            }
        });
    }

    // ───────────────────────── Shell COM / GDI P-Invoke ─────────────────────────

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [Flags]
    private enum SIIGBF : uint
    {
        SIIGBF_RESIZETOFIT = 0x00000000,
        SIIGBF_THUMBNAILONLY = 0x00000008,
    }

    private static class ShellPInvoke
    {
        public static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

        [DllImport("shell32", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHCreateItemFromParsingName(
            string pszPath, IntPtr pbc, ref Guid riid, out IntPtr ppv);

        [DllImport("gdi32")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);
    }
}
