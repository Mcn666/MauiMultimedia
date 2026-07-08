using System;
using System.IO;
using System.Threading.Tasks;
using AVFoundation;
using CoreGraphics;
using CoreMedia;
using Foundation;
using UIKit;
using MauiMultimedia.Viewers.Video;

namespace MauiMultimedia.Viewers.Video.Platforms.iOS;

/// <summary>
/// iOS 原生视频首帧提取：AVAssetImageGenerator（AVFoundation）。
/// 作为 Video 查看器的一部分自包含，不侵入 Shell。
/// 实现 <see cref="IVideoFrameExtractor"/>，由 Video RCL 在运行时自动发现并注册（无需 ModuleInitializer）。
/// </summary>
public class VideoFrameExtractor : IVideoFrameExtractor
{
    private const int ThumbSize = 256;

    // AVAssetImageGenerator 在 .NET iOS 绑定里只有同步 CopyCGImageAtTime，
    // 不生成 *Async 变体（返回值非 bool）。放到线程池执行以免阻塞调用线程。
    public Task<byte[]?> TryExtractAsync(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return Task.FromResult<byte[]?>(null);

        return Task.Run(() =>
        {
            try
            {
                var url = NSUrl.FromFilename(path);
                using var asset = AVAsset.FromUrl(url);
                using var generator = new AVAssetImageGenerator(asset)
                {
                    MaximumSize = new CGSize(ThumbSize, ThumbSize),
                    AppliesPreferredTrackTransform = true
                };

                var requested = CMTime.FromSeconds(1.0, 600);
                NSError? error;
                CMTime actualTime;
                using var cgImage = generator.CopyCGImageAtTime(requested, out actualTime, out error);
                if (cgImage == null || error != null)
                    return (byte[]?)null;

                using var uiImage = UIImage.FromImage(cgImage);
                using var data = uiImage.AsJPEG(0.85f);
                return data?.ToArray();
            }
            catch
            {
                return (byte[]?)null;
            }
        });
    }
}
