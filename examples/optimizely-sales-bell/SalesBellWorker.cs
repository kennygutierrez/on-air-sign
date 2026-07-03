using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SalesBellSample;

/// <summary>
/// Drains the sales-bell queue and performs the actual plug blink (on, wait, off) off the
/// request thread. Runs for the lifetime of the app as a hosted background service.
/// </summary>
public sealed class SalesBellWorker : BackgroundService
{
    private static readonly TimeSpan BlinkDuration = TimeSpan.FromSeconds(3);

    private readonly ChannelReader<byte> _reader;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SalesBellWorker> _log;

    public SalesBellWorker(Channel<byte> channel, IServiceScopeFactory scopes, ILogger<SalesBellWorker> log)
    {
        _reader = channel.Reader;
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var _ in _reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var plug = scope.ServiceProvider.GetRequiredService<IKasaPlug>();

                await plug.SetAsync(true, stoppingToken);
                await Task.Delay(BlinkDuration, stoppingToken);
                await plug.SetAsync(false, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Sales bell blink failed");
            }
        }
    }
}
