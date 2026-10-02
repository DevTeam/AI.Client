namespace AI.Contracts.Updates;

public enum UpdateChannel { Stable, Preview }
public enum UpdatePhase { Idle, Checking, Available, Downloading, Ready, WaitingForTasks, Installing, Failed }

public sealed record UpdatePreferences(bool CheckAutomatically = true, bool DownloadAutomatically = true,
    bool InstallAutomatically = false, UpdateChannel Channel = UpdateChannel.Preview);

public sealed record UpdateRelease(string Version, bool Prerelease, string NotesUrl, string PackageUrl,
    string PackageName, string Sha256, long Size, string? CompanionUrl = null, string? CompanionSha256 = null);

public sealed record UpdateState(string Product, string CurrentVersion, string Runtime, UpdatePreferences Preferences,
    UpdatePhase Phase = UpdatePhase.Idle, UpdateRelease? Release = null, DateTimeOffset? LastChecked = null,
    string? Error = null, string? InstalledVersion = null, bool Supported = true, bool StableAvailable = true,
    string? FailedVersion = null);
