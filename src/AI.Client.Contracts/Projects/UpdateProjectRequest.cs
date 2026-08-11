namespace AI.Client.Contracts.Projects;

public sealed record UpdateProjectRequest(string Name, string Description, long Revision, Guid? ConnectionId = null);
