#r "C:/Users/User/.nuget/packages/microsoft.aspnetcore.components.web/10.0.8/lib/net10.0/Microsoft.AspNetCore.Components.Web.dll"
#r "C:/Users/User/Desktop/Project/MauiMultimedia/Viewers/Html/bin/Debug/net10.0/MauiMultimedia.Viewers.Html.dll"
#r "C:/Users/User/Desktop/Project/MauiMultimedia/Core/bin/Debug/net10.0/MauiMultimedia.Core.dll"

using MauiMultimedia.Viewers.Html.Services;

var path = @"C:/Users/User/Downloads/DeepSeek 开放平台.mhtml";
Console.WriteLine("=== File Info ===");
Console.WriteLine($"Size: {new FileInfo(path).Length} bytes");
Console.WriteLine();

var raw = File.ReadAllBytes(path);
var text = System.Text.Encoding.UTF8.GetString(raw);
var lines = text.Split('\n');
Console.WriteLine($"Total lines: {lines.Length}");
Console.WriteLine();

// Find all lines starting with --
Console.WriteLine("=== Lines starting with -- ===");
for (int i = 0; i < Math.Min(lines.Length, 20); i++)
{
    var t = lines[i].Trim();
    if (t.StartsWith("--"))
        Console.WriteLine($"  Line {i}: [{t.TrimEnd('\r')}]");
}
Console.WriteLine();

// Search for boundary pattern
Console.WriteLine("=== All -- lines in file ===");
var count = 0;
for (int i = 0; i < lines.Length; i++)
{
    var t = lines[i].TrimStart();
    if (t.StartsWith("--") && !t.StartsWith("---"))
    {
        Console.WriteLine($"  Line {i}: [{t.TrimEnd('\r')}]");
        count++;
        if (count > 10) { Console.WriteLine("  ... (truncated)"); break; }
    }
}
