namespace AI.Sandbox;

using System.Text;

/// <summary>
/// A Windows command line that the C runtime splits back into exactly the given arguments, the
/// same quoting <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> uses.
/// </summary>
internal static class CommandLine
{
    public static string Build(string executable, IEnumerable<string> arguments)
    {
        var line = new StringBuilder();
        Append(line, executable);
        foreach (var argument in arguments)
        {
            line.Append(' ');
            Append(line, argument);
        }

        return line.ToString();
    }

    private static void Append(StringBuilder line, string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            line.Append(argument);
            return;
        }

        line.Append('"');
        for (var index = 0; ; index++)
        {
            var backslashes = 0;
            while (index < argument.Length && argument[index] == '\\')
            {
                index++;
                backslashes++;
            }

            if (index == argument.Length)
            {
                // Backslashes before the closing quote are doubled so it stays a quote.
                line.Append('\\', backslashes * 2);
                break;
            }

            if (argument[index] == '"')
            {
                line.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                line.Append('\\', backslashes).Append(argument[index]);
            }
        }

        line.Append('"');
    }
}
