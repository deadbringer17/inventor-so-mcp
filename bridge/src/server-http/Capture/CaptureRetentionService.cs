namespace Inventor.So.Mcp.Http.Capture;

/// <summary>Retire abandoned uploads even when no headset is making requests.</summary>
public sealed class CaptureRetentionService(CaptureTransferStore store, ILogger<CaptureRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { store.PruneExpired(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Paths, tokens, images and scan metadata never enter the log.
                    logger.LogWarning("Reality Capture retention cleanup could not access local storage.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
