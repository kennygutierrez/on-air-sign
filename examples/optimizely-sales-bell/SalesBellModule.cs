using System.Threading.Channels;
using EPiServer.Framework;
using EPiServer.Framework.Initialization;
using EPiServer.ServiceLocation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SalesBellSample;

/// <summary>
/// Registers everything and subscribes the order listener at startup. Depends on Commerce's
/// initialization module so <see cref="EPiServer.Commerce.Order.IOrderEvents"/> is available.
/// </summary>
[InitializableModule]
[ModuleDependency(typeof(EPiServer.Commerce.Initialization.InitializationModule))]
public sealed class SalesBellModule : IConfigurableModule
{
    public void ConfigureContainer(ServiceConfigurationContext context)
    {
        var services = context.Services;

        services.AddOptions<KasaOptions>()
                .Configure<IConfiguration>((o, cfg) => cfg.GetSection("SalesBell").Bind(o));

        // Typed HttpClient — pooled sockets, and a natural spot to add Polly retries later.
        services.AddHttpClient<IKasaPlug, KasaCloudPlug>();

        // One bounded queue shared by the writer (SalesBell) and reader (SalesBellWorker).
        // DropWrite means a Black-Friday burst flickers the light instead of backing up.
        services.AddSingleton(_ => Channel.CreateBounded<byte>(
            new BoundedChannelOptions(capacity: 100) { FullMode = BoundedChannelFullMode.DropWrite }));

        services.AddSingleton<ISalesBell, SalesBell>();
        services.AddHostedService<SalesBellWorker>();
        services.AddSingleton<SalesBellOrderListener>();
    }

    public void Initialize(InitializationEngine context)
        => context.Services.GetInstance<SalesBellOrderListener>().Subscribe();

    public void Uninitialize(InitializationEngine context)
        => context.Services.GetInstance<SalesBellOrderListener>().Unsubscribe();
}
