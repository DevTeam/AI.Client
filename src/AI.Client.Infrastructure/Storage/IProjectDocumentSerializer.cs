using AI.Client.Application.Projects;
using AI.Client.Domain.Projects;

namespace AI.Client.Infrastructure.Storage;

public interface IProjectDocumentSerializer
{
    string Serialize(Project project, long revision);

    StoredProject Deserialize(string json);
}
