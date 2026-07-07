using System;
using System.IO;
using System.Threading.Tasks;
using Android.Graphics;
using Android.Media;
using MauiMultimedia.Core.Abstractions;

namespace MauiMultimedia.Viewers.Video.Platforms.Android;

/// <summary>
/// Android 原生视频首帧提取：MediaMetadataRetriever（系统自带）。
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

        try
        {
            using var retriever = new MediaMetadataRetriever();
            retriever.SetDataSource(path);
            // 取 1 秒处最近关键帧，避免黑屏首帧
            using var frame = retriever.GetFrameAtTime(1_000_000);
            if (frame == null)
                return Task.FromResult<byte[]?>(null);

            float scale = (float)ThumbSize / Math.Max(frame.Width, frame.Height);
            int tw = Math.Max(1, (int)(frame.Width * scale));
            int th = Math.Max(1, (int)(frame.Height * scale));
            using var thumb = Bitmap.CreateScaledBitmap(frame, tw, th, true);
            using var ms = new MemoryStream();
#pragma warning disable CS8604
            thumb.Compress(Bitmap.CompressFormat.Jpeg, 85, ms);
#pragma warning restore CS8604
            return Task.FromResult<byte[]?>(ms.ToArray());
        }
        catch
        {
            return Task.FromResult<byte[]?>(null);
        }
    }
}
