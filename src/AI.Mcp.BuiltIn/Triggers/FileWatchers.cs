namespace AI.Mcp.BuiltIn.Triggers;

/// <summary>
/// One directory's platform notifications. It is a kernel subscription rather than a file
/// operation, so it is the one place in the waiter that stays on the platform (see
/// <c>ADR-013</c>, exception 7); everything it reports is judged through <c>IFileSystem</c>.
/// </summary>
public interface IFileWatcher : IDisposable
{
    /// <summary>An entry was created, changed or deleted; the argument is its full path.</summary>
    event Action<string>? Changed;

    /// <summary>An entry was renamed; the arguments are the old and the new full path.</summary>
    event Action<string, string>? Renamed;

    /// <summary>The platform reported a buffer overflow or another failure.</summary>
    event Action? Faulted;

    void Start();
}

/// <summary>Creates the directory watchers a wait subscribes to.</summary>
public interface IFileWatcherFactory
{
    IFileWatcher Create(string directory, bool recursive);
}

/// <summary>The platform implementation: a <see cref="FileSystemWatcher"/> with the waiter's filters.</summary>
public sealed class SystemFileWatcherFactory : IFileWatcherFactory
{
    public IFileWatcher Create(string directory, bool recursive) => new SystemFileWatcher(directory, recursive);

    private sealed class SystemFileWatcher : IFileWatcher
    {
        private readonly FileSystemWatcher _watcher;
        private readonly FileSystemEventHandler _changed;
        private readonly RenamedEventHandler _renamed;
        private readonly ErrorEventHandler _failed;

        public SystemFileWatcher(string directory, bool recursive)
        {
            _watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = recursive,
                Filter = "*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            // Handlers are held in fields so that disposing detaches exactly what was attached.
            _changed = (_, args) => Changed?.Invoke(args.FullPath);
            _renamed = (_, args) => Renamed?.Invoke(args.OldFullPath, args.FullPath);
            _failed = (_, _) => Faulted?.Invoke();
            _watcher.Changed += _changed;
            _watcher.Created += _changed;
            _watcher.Deleted += _changed;
            _watcher.Renamed += _renamed;
            _watcher.Error += _failed;
        }

        public event Action<string>? Changed;
        public event Action<string, string>? Renamed;
        public event Action? Faulted;

        public void Start() => _watcher.EnableRaisingEvents = true;

        public void Dispose()
        {
            _watcher.Changed -= _changed;
            _watcher.Created -= _changed;
            _watcher.Deleted -= _changed;
            _watcher.Renamed -= _renamed;
            _watcher.Error -= _failed;
            _watcher.Dispose();
        }
    }
}
