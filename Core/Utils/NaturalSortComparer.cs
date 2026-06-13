namespace MauiMultimedia.Core.Utils;

/// <summary>
/// 自然顺序字符串比较器，数字部分按数值而非字符排序。
/// 例如："2" < "11"，"img2" < "img10"。
/// </summary>
public class NaturalSortComparer : IComparer<string>
{
    public static readonly NaturalSortComparer Instance = new();

    public int Compare(string? a, string? b)
    {
        if (a == null || b == null)
            return string.Compare(a, b, StringComparison.Ordinal);
        return NaturalCompare(a, b);
    }

    private static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int numA = 0, numB = 0;
                while (i < a.Length && char.IsDigit(a[i]))
                    numA = numA * 10 + (a[i++] - '0');
                while (j < b.Length && char.IsDigit(b[j]))
                    numB = numB * 10 + (b[j++] - '0');
                if (numA != numB) return numA.CompareTo(numB);
            }
            else
            {
                int cmp = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                if (cmp != 0) return cmp;
                i++; j++;
            }
        }
        return a.Length.CompareTo(b.Length);
    }
}
