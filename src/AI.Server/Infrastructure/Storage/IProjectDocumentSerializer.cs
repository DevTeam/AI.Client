namespace AI.Infrastructure.Storage;

using AI.Application.Projects;
using AI.Domain.Projects;

/// <summary>
/// Reads and writes the on-disk project document. The serializer is stateless, but going through
/// an interface keeps the storage adapter thin and lets tests substitute a serializer for one
/// that has already been pointed at a fixture directory.
/// </summary>
public interface IProjectDocumentSerializer
{
    string Serialize(Project project, long revision);

    StoredProject Deserialize(string json);
}
