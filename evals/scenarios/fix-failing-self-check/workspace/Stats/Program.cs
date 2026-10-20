// Self-checks of the statistics helpers. The program exits with 1 when any check fails.
var failures = 0;

Expect("median of odd count", Statistics.Median([5, 1, 3]), 3);
Expect("median of even count", Statistics.Median([4, 1, 3, 2]), 2.5);
Expect("median of one value", Statistics.Median([7]), 7);
Expect("mean", Statistics.Mean([1, 2, 3, 4]), 2.5);

Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
return failures == 0 ? 0 : 1;

void Expect(string name, double actual, double expected)
{
    if (Math.Abs(actual - expected) < 1e-9) return;
    failures++;
    Console.WriteLine($"FAIL {name}: expected {expected}, got {actual}");
}
