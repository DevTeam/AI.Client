namespace AI.Updates;

using System.Globalization;

/// <summary>SemVer ordering, including numeric prerelease identifiers; build metadata is ignored.</summary>
public sealed class UpdateVersion
{
    private readonly int[] _numbers;
    private readonly string[] _preview;

    private UpdateVersion(int[] numbers, string[] preview) => (_numbers, _preview) = (numbers, preview);

    public static UpdateVersion? Parse(string value)
    {
        var parts = value.TrimStart('v', 'V').Split('+')[0].Split('-', 2);
        var numbers = parts[0].Split('.');
        if (numbers.Length is < 3 or > 4) return null;
        var result = new int[4];
        for (var i = 0; i < numbers.Length; i++)
            if (!int.TryParse(numbers[i], NumberStyles.None, CultureInfo.InvariantCulture, out result[i])) return null;
        var preview = parts.Length == 2 ? parts[1].Split('.') : [];
        if (preview.Any(part => part.Length == 0 || part.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-'))) return null;
        return new UpdateVersion(result, preview);
    }

    public int CompareTo(UpdateVersion? other)
    {
        if (other is null) return 1;
        for (var i = 0; i < _numbers.Length; i++)
            if (_numbers[i].CompareTo(other._numbers[i]) is var difference && difference != 0) return difference;
        if (_preview.Length == 0 || other._preview.Length == 0)
            return (_preview.Length == 0 ? 1 : 0).CompareTo(other._preview.Length == 0 ? 1 : 0);
        for (var i = 0; i < Math.Min(_preview.Length, other._preview.Length); i++)
        {
            var leftNumeric = ulong.TryParse(_preview[i], NumberStyles.None, CultureInfo.InvariantCulture, out var left);
            var rightNumeric = ulong.TryParse(other._preview[i], NumberStyles.None, CultureInfo.InvariantCulture, out var right);
            var difference = leftNumeric && rightNumeric ? left.CompareTo(right)
                : leftNumeric != rightNumeric ? (leftNumeric ? -1 : 1)
                : string.CompareOrdinal(_preview[i], other._preview[i]);
            if (difference != 0) return difference;
        }
        return _preview.Length.CompareTo(other._preview.Length);
    }
}
