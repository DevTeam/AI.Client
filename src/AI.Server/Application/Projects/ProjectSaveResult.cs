namespace AI.Application.Projects;

public sealed record ProjectSaveResult(bool IsSaved, long Revision)
{
    public static ProjectSaveResult Saved(long revision) => new(true, revision);

    public static ProjectSaveResult Conflict(long revision) => new(false, revision);
}
