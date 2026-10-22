namespace AI.Application.Chats;

using System.Text;

/// <summary>
/// Keys are strings of base-62 digits whose ordinal order matches the digit order.
/// </summary>
/// <remarks>
/// A key never ends in the lowest digit: "a0" would leave no room below it without growing to the
/// left, which ordinal comparison cannot express. Keeping that invariant is what makes
/// <see cref="Between"/> total.
/// </remarks>
public sealed class PinOrderKeys : IPinOrderKeys
{
    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public string Between(string? lower, string? upper)
    {
        if (lower is not null) EnsureValid(lower);
        if (upper is not null) EnsureValid(upper);
        if (lower is not null && upper is not null && string.CompareOrdinal(lower, upper) >= 0)
            throw new ArgumentException("A pin order key range must be ascending.", nameof(upper));

        var result = new StringBuilder();
        var bounded = upper is not null;
        for (var position = 0; ; position++)
        {
            var low = lower is not null && position < lower.Length ? Digits.IndexOf(lower[position]) : 0;
            var high = bounded && position < upper!.Length ? Digits.IndexOf(upper[position]) : Digits.Length;
            if (low == high)
            {
                result.Append(Digits[low]);
                continue;
            }

            var middle = (low + high) / 2;
            if (middle > low)
            {
                result.Append(Digits[middle]);
                return result.ToString();
            }

            // Adjacent digits: take the lower one, after which the prefix is already below the
            // upper bound and only the lower key still constrains the remaining digits.
            result.Append(Digits[low]);
            bounded = false;
        }
    }

    private static void EnsureValid(string key)
    {
        if (key.Length == 0 || key[^1] == Digits[0] || key.Any(digit => Digits.IndexOf(digit) < 0))
            throw new ArgumentException($"Pin order key '{key}' is invalid.", nameof(key));
    }
}
