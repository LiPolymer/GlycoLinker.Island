using System.Runtime.CompilerServices;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Services.Registry;
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
using GlycoLinker.Island.Sai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SuperAutoIsland.Interface.Services;

namespace GlycoLinker.Island;

public record PingRespondModel(string Message);

[PluginEntrance]
// ReSharper disable once ClassNeverInstantiated.Global
public class Plugin : PluginBase {
    public override void Initialize(HostBuilderContext context, IServiceCollection services) {
        Config.SaveDist = Path.Combine(PluginConfigFolder, "glycolinker.json");
        Config.Instance = Config.Load();
        services.AddSingleton(new GlycoSaiNodeStore(Config.Instance));
        services.AddHostedService<ServicesCaptureService>();
        services.AddSingleton(_ => new GlycoService(Config.Instance.Gid));
        services.AddHostedService<GlycoBridge>();
        services.AddAction<GlycoCallAction, GlycoCallSettings>();
        services.AddAction<GlycoEmitterAction, GlycoEmitterSettings>();
        services.AddTrigger<GlycoTrigger, GlycoTriggerSettings>();
        services.AddTrigger<GlycoEventTrigger, GlycoEventSettings>();
        services.AddSettingsPage<SettingsPage>();
        // Prepare DI before the container is built; publish navigation only after SAI registration succeeds.
        services.AddKeyedTransient<SettingsPageBase, GlycoSaiSettingsPage>("glycolinker.sai.nodes");
        AppBase.Current.AppStarted += async (_,_) => {
            GlycoComplex? gx = IAppHost.TryGetService<GlycoService>();
            if (gx == null) {
                ServicesCaptureService.Logger?.LogError("Glycoprotein 服务未注册. 等等, 什么???");
                return;
            }
            ServicesCaptureService.Logger?.LogInformation("正在启动 Glycoprotein 服务");
            gx.Vendor = $"ClassIsland {AppBase.AppVersion} {AppBase.AppCodeName} GL";
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
        AppBase.Current.AppStarted += async (_, _) => {
            try {
                ISaiServer? sai = IAppHost.TryGetService<ISaiServer>();
                if (sai == null) {
                    ServicesCaptureService.Logger?.LogInformation("未检测到 SuperAutoIsland (ISaiServer), 跳过 SAI 积木注册");
                    return;
                }
                GlycoSaiNodeStore store = IAppHost.GetService<GlycoSaiNodeStore>();
                await Dispatcher.UIThread.InvokeAsync(() => RegisterSai(sai, store));
            } catch (Exception e) {
                ServicesCaptureService.Logger?.LogError(e, "注册 SuperAutoIsland 积木失败, 已跳过");
            }
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterSai(ISaiServer sai, GlycoSaiNodeStore store) {
        sai.AddCategory(new GlycoSaiCategoryProvider(store));
        sai.AddPrefixHandler(GlycoSaiIds.DataPrefix, GlycoSaiHandlers.Handle);
        sai.AddPrefixHandler(GlycoSaiIds.RulePrefix, GlycoSaiHandlers.Handle);
        sai.RegisterDynamicDropdown(GlycoSaiIds.NodesDropdownId, GlycoSaiHandlers.GetNodeOptionsAsync);
        GlycoSaiRefresher.Attach(sai, store);
        store.Attach(GlycoBridge.Instance);
        if (!SettingsWindowRegistryService.Registered.Any(page => page.Id == "glycolinker.sai.nodes"))
            SettingsWindowRegistryService.Registered.Add(new SettingsPageInfo(
                "glycolinker.sai.nodes", "GlycoLinker · SAI 节点", "\uE63D", "\uEA36"));
        ServicesCaptureService.Logger?.LogInformation("已向 SuperAutoIsland 注册 GlycoLinker 分类、前缀、节点下拉框、保存定义刷新及节点设置页");
    }
}