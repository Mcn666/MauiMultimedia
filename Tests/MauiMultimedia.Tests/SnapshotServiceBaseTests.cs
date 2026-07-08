using System.IO;
using System.Threading.Tasks;
using MauiMultimedia.Core.Services;
using Xunit;

namespace MauiMultimedia.Tests;

/// <summary>
/// SnapshotServiceBase 的缓存 / 限流骨架验证。
/// 用伪造子类统计 GenerateAsync 调用次数，确认缓存命中不再触发生成。
/// </summary>
public class SnapshotServiceBaseTests
{
    private sealed class FakeGenerator : SnapshotServiceBase
    {
        public int CallCount;
        private readonly string? _return;
        public FakeGenerator(string? ret) => _return = ret;

        protected override Task<string?> GenerateAsync(string filePath)
        {
            CallCount++;
            return Task.FromResult<string?>(_return);
        }
    }

    [Fact]
    public async Task 缓存命中_只生成一次()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var g = new FakeGenerator("data:image/jpeg;base64,ABC");

            var r1 = await g.GetSnapshotAsync(tmp);
            var r2 = await g.GetSnapshotAsync(tmp);

            Assert.Equal("data:image/jpeg;base64,ABC", r1);
            Assert.Equal(r1, r2);          // 两次返回一致
            Assert.Equal(1, g.CallCount);  // 第二次命中缓存，未再生成
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task 空路径或文件不存在_返回null且不生成()
    {
        var g = new FakeGenerator("data:image/jpeg;base64,ABC");

        Assert.Null(await g.GetSnapshotAsync(""));
        Assert.Null(await g.GetSnapshotAsync("C:/no/such/file/xyz123.png"));
        Assert.Equal(0, g.CallCount);
    }

    [Fact]
    public async Task 生成返回null_不写入缓存且返回null()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var g = new FakeGenerator(null);

            Assert.Null(await g.GetSnapshotAsync(tmp));
            Assert.Equal(1, g.CallCount);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
