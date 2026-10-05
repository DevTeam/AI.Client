namespace AI.Application.Settings;

using AI.Contracts.Settings;

public interface IConnectionImageProbe
{
    Task<ImageProbeResult> TestAsync(Guid connectionId, ImageProbeRequest request, CancellationToken cancellationToken);
}
