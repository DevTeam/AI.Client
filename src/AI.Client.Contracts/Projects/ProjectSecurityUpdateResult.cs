namespace AI.Client.Contracts.Projects;

public sealed record ProjectSecurityUpdateResult(ProjectUpdateStatus Status, ProjectDetails? Project, long Revision)
{
    public static ProjectSecurityUpdateResult Updated(ProjectDetails project) => new(ProjectUpdateStatus.Updated, project, project.Revision);

    public static ProjectSecurityUpdateResult Conflict(long revision) => new(ProjectUpdateStatus.Conflict, null, revision);

    public static ProjectSecurityUpdateResult NotFound() => new(ProjectUpdateStatus.NotFound, null, 0);
}
