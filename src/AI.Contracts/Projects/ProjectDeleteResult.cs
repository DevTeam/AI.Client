namespace AI.Contracts.Projects;

public sealed record ProjectDeleteResult(bool IsDeleted, long Revision)
{
    public static ProjectDeleteResult Deleted(long revision) => new(true, revision);

    public static ProjectDeleteResult Conflict(long revision) => new(false, revision);

    public static ProjectDeleteResult NotFound() => new(false, 0);
}
