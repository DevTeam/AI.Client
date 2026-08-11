using AI.Client.Domain.Projects;

namespace AI.Client.Application.Projects;

public sealed record StoredProject(Project Project, long Revision);
