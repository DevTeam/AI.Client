namespace AI.Contracts.Projects;

public sealed record ProjectUpdateResult(ProjectUpdateStatus Status, ProjectDetails? Project, long Revision)
{
    public static ProjectUpdateResult Updated(ProjectDetails project) => new(ProjectUpdateStatus.Updated, project, project.Revision);

    public static ProjectUpdateResult Conflict(long revision) => new(ProjectUpdateStatus.Conflict, null, revision);

    public static ProjectUpdateResult NotFound() => new(ProjectUpdateStatus.NotFound, null, 0);
}
