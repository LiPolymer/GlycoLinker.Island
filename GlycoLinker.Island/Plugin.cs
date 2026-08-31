using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Shared;
using Glycoprotein;
using Glycoprotein.Glycosylation;
using Glycoprotein.HostedService;
using GlycoLinker.Island.Automations;
using GlycoLinker.Island.Automations.Actions;
using GlycoLinker.Island.Automations.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GlycoLinker.Island;

public record PingRespondModel(string Message);

[PluginEntrance]
// ReSharper disable once ClassNeverInstantiated.Global
public class Plugin : PluginBase {
    public override void Initialize(HostBuilderContext context, IServiceCollection services) {
        Config.SaveDist = Path.Combine(PluginConfigFolder, "glycolinker.json");
        Config.Instance = Config.Load();
        services.AddHostedService<ServicesCaptureService>();
        services.AddSingleton(_ => new GlycoService(Config.Instance.Gid));
        services.AddHostedService<GlycoBridge>();
        services.AddAction<GlycoCallAction, GlycoCallSettings>();
        services.AddAction<GlycoEmitterAction, GlycoEmitterSettings>();
        services.AddTrigger<GlycoTrigger, GlycoTriggerSettings>();
        services.AddTrigger<GlycoEventTrigger, GlycoEventSettings>();
        services.AddSettingsPage<SettingsPage>();
        AppBase.Current.AppStarted += async (_,_) => {
            GlycoComplex? gx = IAppHost.TryGetService<GlycoService>();
            if (gx == null) {
                ServicesCaptureService.Logger?.LogError("Glycoprotein 服务未注册. 等等, 什么???");
                return;
            }
            ServicesCaptureService.Logger?.LogInformation("正在启动 Glycoprotein 服务");
            gx.OnDiscovered += beacon => {
                ServicesCaptureService.GLogger?.LogInformation("发现G节点:[{BeaconId}]拥有[{FieldsCount}]个域", beacon.Id, beacon.Fields.Count);
            };
            gx.OnChanged += beacon => {
                ServicesCaptureService.GLogger?.LogInformation("G节点字段变更:[{BeaconId}]拥有[{FieldsCount}]个域", beacon.Id, beacon.Fields.Count);
            };
            gx.OnExpired += beacon => {
                ServicesCaptureService.GLogger?.LogInformation("G节点过期:[{beaconId}]", beacon.Id);
            };
            await gx.AddFunction(new Field.Method {
                Id = "ping",
                FriendlyName = "Ping",
                Description = "测试连通性"
            },() => {
                ServicesCaptureService.GLogger?.LogInformation("Received Ping!");
                return new PingRespondModel("Pong!");
            }).StartAsync();
            ServicesCaptureService.Logger?.LogInformation("Glycoprotein 服务已启动, 作为[{GxId}]", gx.Id);
        };
    }
}