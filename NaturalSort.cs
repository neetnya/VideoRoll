namespace VideoRoll;

/// <summary>数字感知的自然排序（例如 "第2集" 排在 "第10集" 之前）。</summary>
public static class NaturalSort
{
    public static int Compare(string? a, string? b)
    {
        a ??= "";
        b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            char ca = a[i], cb = b[j];
            if (char.IsDigit(ca) && char.IsDigit(cb))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;

                string na = a.Substring(si, i - si).TrimStart('0');
                string nb = b.Substring(sj, j - sj).TrimStart('0');
                if (na.Length != nb.Length)
                    return na.Length.CompareTo(nb.Length);
                int c = string.CompareOrdinal(na, nb);
                if (c != 0)
                    return c;
                if (i - si != j - sj)
                    return (i - si).CompareTo(j - sj);
            }
            else
            {
                int c = char.ToLowerInvariant(ca).CompareTo(char.ToLowerInvariant(cb));
                if (c != 0)
                    return c;
                i++;
                j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
