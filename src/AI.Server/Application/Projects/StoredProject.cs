namespace AI.Application.Projects;

using AI.Domain.Projects;

public sealed record StoredProject(Project Project, long Revision);
