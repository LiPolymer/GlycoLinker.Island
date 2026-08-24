using Glycoprotein;
using Glycoprotein.HostedService;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GlycoLinker.Island;

public class ServicesCaptureService : IHostedService {
    public static ILogger<Plugin>? Logger;
    public static ILogger<GlycoService>? GLogger;
    
    public ServicesCaptureService(ILogger<Plugin> logger, ILogger<GlycoService> glogger) {
        Logger = logger;
        GLogger = glogger;
    }
    
    public Task StartAsync(CancellationToken cancellationToken) {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        return Task.CompletedTask;
    }
}