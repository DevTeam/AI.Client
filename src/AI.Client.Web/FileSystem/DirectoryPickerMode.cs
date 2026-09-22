namespace AI.Client.Web.FileSystem;

/// <summary>What the picker is being asked for. The walk is the same; only the last click differs.</summary>
public enum DirectoryPickerMode
{
    /// <summary>The folder you are standing in is the answer. Files are not listed at all.</summary>
    Directory,

    /// <summary>A file in the folder you are standing in is the answer. Folders are still how you get there.</summary>
    File
}
