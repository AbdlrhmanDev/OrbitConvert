using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.History;
using OrbitConvert.Core.Processes;
using OrbitConvert.Core.Queue;
using OrbitConvert.Core.Settings;
using OrbitConvert.Core.Tools;
using OrbitConvert.Infrastructure.Persistence;
using OrbitConvert.Infrastructure.Processes;
using OrbitConvert.Infrastructure.Tools;
using OrbitConvert.Infrastructure.Updates;

namespace OrbitConvert.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Registers the conversion engine, queue, persistence and tool discovery.</summary>
    public static IServiceCollection AddOrbitConvertInfrastructure(this IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IHistoryStore, JsonHistoryStore>();
        services.AddSingleton<IFileTypeDetector, FileTypeDetector>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IToolLocator>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsStore>();
            return new ToolLocator(paths, () => settings.Current.ToolPaths, sp.GetRequiredService<ILogger<ToolLocator>>());
        });

        services.AddSingleton<TempWorkspaceProvider>();
        services.AddSingleton<IWorkspaceProvider>(sp => sp.GetRequiredService<TempWorkspaceProvider>());
        services.AddSingleton<OutputValidator>();
        services.AddSingleton<ConversionEngine>();
        services.AddSingleton<ConversionCapabilityRegistry>();
        services.AddSingleton(sp => new ConversionQueue(
            sp.GetRequiredService<ConversionEngine>(),
            sp.GetRequiredService<ILogger<ConversionQueue>>(),
            sp.GetRequiredService<ISettingsStore>().Current.MaxParallelJobs));

        // A single HttpClient used only for user-initiated downloads and opt-in update checks.
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(30) });
        services.AddSingleton<DependencyInstaller>();
        services.AddSingleton<UpdateChecker>();
        return services;
    }
}
