namespace AI.Host;

internal static class Program
{
    // The tray icon's UI must run on this thread: macOS allows no other, and Windows wants an STA one.
    [STAThread]
    public static int Main(string[] args) => new CommandLineComposition(args).Root.Run();
}
