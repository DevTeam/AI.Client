namespace AI.Client.Application.Projects;

using AI.Client.Domain.Projects;

public sealed record StoredProject(Project Project, long Revision);
