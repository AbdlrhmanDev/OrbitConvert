using Microsoft.Extensions.DependencyInjection;
using OrbitConvert.Converters.Archives;
using OrbitConvert.Converters.Documents;
using OrbitConvert.Converters.Images;
using OrbitConvert.Converters.Media;
using OrbitConvert.Converters.Pdf;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Infrastructure;

namespace OrbitConvert.Converters;

public static class ConvertersServiceCollectionExtensions
{
    /// <summary>The complete conversion stack shared by the desktop app and the command line.</summary>
    public static IServiceCollection AddOrbitConvert(this IServiceCollection services, Infrastructure.AppPaths paths) =>
        services.AddOrbitConvertInfrastructure(paths).AddOrbitConvertConverters();

    /// <summary>Registers every built-in converter. Additional engines only need to register an <see cref="IConverter"/>.</summary>
    public static IServiceCollection AddOrbitConvertConverters(this IServiceCollection services)
    {
        services.AddSingleton<FfmpegCapabilities>();
        services.AddSingleton<ImageMagickConverter>();
        services.AddSingleton<FfmpegConverter>();
        services.AddSingleton<PandocConverter>();
        services.AddSingleton<LibreOfficeConverter>();
        services.AddSingleton<MarkupToPdfConverter>();
        services.AddSingleton<PdfToolsConverter>();
        services.AddSingleton<ArchiveConverter>();

        services.AddSingleton<IConverter>(sp => sp.GetRequiredService<ImageMagickConverter>());
        services.AddSingleton<IConverter>(sp => sp.GetRequiredService<FfmpegConverter>());
        services.AddSingleton<IConverter>(sp => sp.GetRequiredService<PandocConverter>());
        services.AddSingleton<IConverter>(sp => sp.GetRequiredService<LibreOfficeConverter>());
        services.AddSingleton<IConverter>(sp => sp.GetRequiredService<MarkupToPdfConverter>());
        services.AddSingleton<IConverter>(sp => sp.GetRequiredService<PdfToolsConverter>());
        services.AddSingleton<IConverter>(sp => sp.GetRequiredService<ArchiveConverter>());
        return services;
    }
}
