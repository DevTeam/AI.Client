namespace AI.Desktop;

using System.Text.Json;
using System.Text.Json.Nodes;
using AI.Contracts.FileSystem;
using Avalonia.Platform;
using Microsoft.Win32;

/// <summary>
/// WebView2 has no settings page and no API for spellcheck languages, and it checks only the
/// language it runs in, usually English. Two changes make it check every language from Windows'
/// Language settings:
/// <list type="bullet">
/// <item>At startup the engine trims the profile's dictionary list back to its own language, but
/// adds the forced list that policies use and never trims it; that list is rewritten before each
/// start to follow Windows.</item>
/// <item>Through the Windows spellchecker the engine checks the other languages' words with the
/// English checker, so every Cyrillic word came out misspelled. Without it each language gets its
/// own checker.</item>
/// </list>
/// </summary>
internal sealed class WebView2Spellcheck(IFileSystem files) : IWebViewSpellcheck
{
    private const string OwnCheckers = "--disable-features=WinUseBrowserSpellChecker";

    public void Configure(WindowsWebView2EnvironmentRequestedEventArgs environment)
    {
        if (!OperatingSystem.IsWindows() || environment.UserDataFolder is not { } userDataFolder) return;
        var languages = WindowsLanguages();
        if (languages.Length == 0 || !Force(userDataFolder, languages) || languages.Length == 1) return;
        environment.AdditionalBrowserArguments = string.IsNullOrWhiteSpace(environment.AdditionalBrowserArguments)
            ? OwnCheckers
            : environment.AdditionalBrowserArguments + " " + OwnCheckers;
    }

    /// <summary>False when the profile could not be changed, so the engine keeps its usual checker.</summary>
    private bool Force(string userDataFolder, string[] languages)
    {
        var directory = Path.Combine(userDataFolder, "EBWebView", "Default");
        var path = Path.Combine(directory, "Preferences");
        try
        {
            var json = SynchronousFiles.Complete(() => files.ReadTextAsync(path, CancellationToken.None));
            var preferences = json is null ? new JsonObject() : JsonNode.Parse(json) as JsonObject;
            if (preferences is null) return false;
            if (preferences["spellcheck"] is not JsonObject spellcheck) preferences["spellcheck"] = spellcheck = new JsonObject();
            var forced = (spellcheck["forced_dictionaries"] as JsonArray)?.Select(item => item?.GetValue<string>()).ToArray();
            if (forced is not null && forced.SequenceEqual(languages)) return true;
            spellcheck["forced_dictionaries"] = new JsonArray(languages.Select(language => (JsonNode)language).ToArray());
            SynchronousFiles.Complete(() => files.CreateDirectoryAsync(directory, false, CancellationToken.None));
            SynchronousFiles.Complete(() => files.WriteTextAsync(path + ".tmp", preferences.ToJsonString(), CancellationToken.None));
            SynchronousFiles.Complete(() => files.MoveAsync(path + ".tmp", path, true, CancellationToken.None));
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>The languages listed in Windows' Language settings, preferred first.</summary>
    private static string[] WindowsLanguages()
    {
        if (!OperatingSystem.IsWindows()) return [];
        using var profile = Registry.CurrentUser.OpenSubKey(@"Control Panel\International\User Profile");
        return profile?.GetValue("Languages") is string[] languages
            ? languages.Where(language => language.Length is > 0 and <= 35).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
    }
}
