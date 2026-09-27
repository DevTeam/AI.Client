namespace AI.Desktop;

internal static class Program
{
    // The UI must start on this thread: macOS allows no other, and WebView2 needs an STA one.
    [STAThread]
    public static int Main(string[] args) => new CommandLineComposition(args).Root.Run();
}
