namespace AI.Host;

/// <summary>Starts the background Host when it is installed but not running.</summary>
internal interface IHostProcess
{
    void StartInBackground();
}
