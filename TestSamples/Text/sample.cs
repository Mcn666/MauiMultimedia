// C# 测试样本
using System;
using System.Collections.Generic;
using System.Linq;

namespace MauiMultimedia.Samples
{
    public class SampleService
    {
        private readonly Dictionary<string, int> _counts = new();

        public void Register(string key)
        {
            _counts[key] = _counts.GetValueOrDefault(key) + 1;
        }

        public IEnumerable<string> TopKeys(int n) =>
            _counts.OrderByDescending(kv => kv.Value).Take(n).Select(kv => kv.Key);

        public static string Hello(string name) => $"Hello, {name}!";
    }
}
