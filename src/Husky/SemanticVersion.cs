using System.Globalization;

namespace Husky;

/// <summary>
/// Lightweight SemVer comparator (LEASH §9.2/§9.3 — both providers use the
/// same comparison rule). Accepts a leading <c>v</c> prefix on either side.
/// Pre-release tags are treated lexicographically per SemVer §11; build
/// metadata is stripped before comparison.
/// </summary>
internal readonly record struct SemanticVersion(int Major, int Minor, int Patch, string PreRelease)
    : IComparable<SemanticVersion>
{
    public static SemanticVersion Parse(string value)
    {
        if (!TryParse(value, out SemanticVersion version))
            throw new FormatException($"Not a valid SemVer version: '{value}'.");
        return version;
    }

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        ReadOnlySpan<char> span = value.AsSpan().Trim();
        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V')) span = span[1..];

        int plus = span.IndexOf('+');
        if (plus >= 0) span = span[..plus];

        string preRelease = string.Empty;
        int dash = span.IndexOf('-');
        if (dash >= 0)
        {
            preRelease = span[(dash + 1)..].ToString();
            span = span[..dash];
        }

        Span<int> parts = stackalloc int[3];
        int index = 0;
        foreach (Range range in SplitDots(span))
        {
            if (index >= 3) return false;
            if (!int.TryParse(span[range], NumberStyles.None, CultureInfo.InvariantCulture, out int part))
                return false;
            parts[index++] = part;
        }

        if (index == 0) return false;

        version = new SemanticVersion(
            Major: parts[0],
            Minor: index > 1 ? parts[1] : 0,
            Patch: index > 2 ? parts[2] : 0,
            PreRelease: preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        int cmp = Major.CompareTo(other.Major);
        if (cmp != 0) return cmp;
        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0) return cmp;
        cmp = Patch.CompareTo(other.Patch);
        if (cmp != 0) return cmp;

        // SemVer §11: a version with a pre-release is *less than* one without.
        bool leftHas = PreRelease.Length > 0;
        bool rightHas = other.PreRelease.Length > 0;
        if (leftHas && !rightHas) return -1;
        if (!leftHas && rightHas) return 1;
        if (!leftHas && !rightHas) return 0;

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    private static int ComparePreRelease(string left, string right)
    {
        string[] leftParts = left.Split('.');
        string[] rightParts = right.Split('.');
        int common = Math.Min(leftParts.Length, rightParts.Length);
        for (int i = 0; i < common; i++)
        {
            int cmp = ComparePreReleasePart(leftParts[i], rightParts[i]);
            if (cmp != 0) return cmp;
        }
        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private static int ComparePreReleasePart(string left, string right)
    {
        bool leftNumeric = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out int leftNum);
        bool rightNumeric = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out int rightNum);
        if (leftNumeric && rightNumeric) return leftNum.CompareTo(rightNum);
        if (leftNumeric) return -1;
        if (rightNumeric) return 1;
        return string.CompareOrdinal(left, right);
    }

    private static List<Range> SplitDots(ReadOnlySpan<char> span)
    {
        List<Range> ranges = [];
        int start = 0;
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] != '.') continue;
            ranges.Add(new Range(start, i));
            start = i + 1;
        }
        ranges.Add(new Range(start, span.Length));
        return ranges;
    }
}
