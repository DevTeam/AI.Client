namespace AI.Desktop;

using Avalonia.Platform;

/// <summary>Chooses the languages the web view underlines misspelled words in.</summary>
internal interface IWebViewSpellcheck
{
    /// <summary>Called before the engine starts on the profile in <see cref="WindowsWebView2EnvironmentRequestedEventArgs.UserDataFolder"/>.</summary>
    void Configure(WindowsWebView2EnvironmentRequestedEventArgs environment);
}
