using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrbitConvert.App.Converters;
using OrbitConvert.App.Services;
using OrbitConvert.Converters.Archives;
using OrbitConvert.Converters.Pdf;
using OrbitConvert.Core.Formats;

namespace OrbitConvert.App.ViewModels;

public sealed record ToolCard(string Title, string Description, string Glyph, string Category, string TargetKey,
    IReadOnlyList<FileFormat> Accepts, bool Multiple);

/// <summary>Entry points for PDF and archive operations. Each card collects files and opens the Convert page.</summary>
public sealed partial class ToolsViewModel(DialogService dialogs, Lazy<IMainWindowHost> host) : ObservableObject
{
    private static readonly FileFormat[] Images =
        [KnownFormats.Jpeg, KnownFormats.Png, KnownFormats.WebP, KnownFormats.Bmp, KnownFormats.Tiff, KnownFormats.Gif, KnownFormats.Heic, KnownFormats.Avif];

    public Action<Page>? Navigate { get; set; }

    public IReadOnlyList<ToolCard> PdfTools { get; } =
    [
        new("Merge PDFs", "Combine several PDFs into one, in the order you choose.", "", "Pdf", PdfToolsConverter.MergeKey, [KnownFormats.Pdf], true),
        new("Split PDF", "Save every page as its own PDF.", "", "Pdf", PdfToolsConverter.SplitKey, [KnownFormats.Pdf], true),
        new("Extract pages", "Copy selected pages (e.g. 1-3, 7) into a new PDF.", "", "Pdf", PdfToolsConverter.ExtractKey, [KnownFormats.Pdf], true),
        new("Rotate pages", "Rotate all or some pages by 90°, 180° or 270°.", "", "Pdf", PdfToolsConverter.RotateKey, [KnownFormats.Pdf], true),
        new("Compress PDF", "Make PDFs smaller. Strong modes need Ghostscript.", "", "Pdf", PdfToolsConverter.CompressKey, [KnownFormats.Pdf], true),
        new("Images to PDF", "One PDF with a page per image.", "", "Image", PdfToolsConverter.CombineImagesKey, Images, true),
        new("PDF to images", "Render pages as PNG, JPG or WebP.", "", "Pdf", "png", [KnownFormats.Pdf], true),
    ];

    public IReadOnlyList<ToolCard> ArchiveTools { get; } =
    [
        new("Create ZIP", "Compress files into a .zip archive.", "", "Archive", "zip", [], true),
        new("Create 7z", "Compress files into a .7z archive (needs 7-Zip).", "", "Archive", "7z", [], true),
        new("Extract archive", "Safely extract ZIP or 7z into a new folder.", "", "Archive", ArchiveConverter.ExtractKey,
            [KnownFormats.Zip, KnownFormats.SevenZip], true),
    ];

    [ObservableProperty]
    private string? _inspectTitle;

    [ObservableProperty]
    private string? _inspectResult;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private void Run(ToolCard card)
    {
        var files = card.Accepts.Count == 0
            ? dialogs.PickFiles(card.Title, (FormatCategory?)null, card.Multiple)
            : dialogs.PickFiles(card.Title, card.Accepts, card.Multiple);
        if (files.Count > 0)
        {
            host.Value.ShowConvert(files, card.TargetKey);
        }
    }

    [RelayCommand]
    private async Task InspectPdf()
    {
        var file = dialogs.PickFiles("Choose a PDF", [KnownFormats.Pdf], multiple: false).FirstOrDefault();
        if (file is null)
        {
            return;
        }
        IsBusy = true;
        try
        {
            var info = await Task.Run(() => PdfInspector.Inspect(file));
            InspectTitle = Path.GetFileName(file);
            if (info.Error is not null)
            {
                InspectResult = info.Error;
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine($"Pages: {info.PageCount}    PDF version: {info.Version}    Size: {FileSizeConverter.Format(info.FileSize)}");
            if (info.PageSizes.Count > 0)
            {
                var (w, h) = info.PageSizes[0];
                sb.AppendLine($"Page size: {w / 72 * 25.4:0} × {h / 72 * 25.4:0} mm ({w:0} × {h:0} pt)");
            }
            Append(sb, "Title", info.Title);
            Append(sb, "Author", info.Author);
            Append(sb, "Subject", info.Subject);
            Append(sb, "Keywords", info.Keywords);
            Append(sb, "Created with", info.Creator);
            Append(sb, "Producer", info.Producer);
            Append(sb, "Created", info.Created?.ToString("g"));
            Append(sb, "Modified", info.Modified?.ToString("g"));
            InspectResult = sb.ToString().TrimEnd();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestArchive()
    {
        var file = dialogs.PickFiles("Choose an archive to verify", [KnownFormats.Zip, KnownFormats.SevenZip], multiple: false).FirstOrDefault();
        if (file is null)
        {
            return;
        }
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => ArchiveConverter.Test(file));
            InspectTitle = Path.GetFileName(file);
            var sb = new StringBuilder();
            sb.AppendLine(result.IsValid ? "✓ Archive is intact (all checksums match)." : "✗ Problems were found:");
            sb.AppendLine($"{result.Entries} entries, {FileSizeConverter.Format(result.UncompressedBytes)} uncompressed");
            foreach (var problem in result.Problems.Take(20))
            {
                sb.AppendLine("• " + problem);
            }
            InspectResult = sb.ToString().TrimEnd();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void Append(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            sb.AppendLine($"{label}: {value}");
        }
    }
}
