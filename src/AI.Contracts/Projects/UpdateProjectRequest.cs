namespace AI.Contracts.Projects;

public sealed record UpdateProjectRequest(string Name, string Description, long Revision, Guid? ConnectionId = null, bool UseDefaultConnection = false);
