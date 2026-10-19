using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrbitConvert.App.Converters;
using OrbitConvert.App.Services;
using OrbitConvert.Converters.Archives;
using OrbitConvert.Converters.Images;
using OrbitConvert.Converters.Pdf;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Tools;

namespace OrbitConvert.App.ViewModels;

public enum ToolsMode { Pdf, Image }

/// <summary>What a tool card does when clicked.</summary>
public enum ToolAction { Convert, Crop, Background, InspectPdf, TestArchive, Planned }

/// <summary>
/// A tool card. <see cref="Icon"/> is 24x24 stroke path data from the design. Planned cards are
/// shown disabled so it is clear the feature does not exist yet.
/// </summary>
public sealed record ToolCard(string Title, string Description, string Icon, string Category, ToolAction Action,
    string? TargetKey = null, IReadOnlyList<FileFormat>? Accepts = null, bool Multiple = true)
{
    public bool Available => Action != ToolAction.Planned;
    public string Tag { get; init; } = "Ready";
    public bool TagIsWarning { get; init; }
}

/// <summary>PDF Tools and Image Tools pages. Conversion tools collect files and open the Convert page.</summary>
public sealed partial class ToolsViewModel : ObservableObject
{
    private static readonly FileFormat[] Images =
        [KnownFormats.Jpeg, KnownFormats.Png, KnownFormats.WebP, KnownFormats.Bmp, KnownFormats.Tiff, KnownFormats.Gif, KnownFormats.Heic, KnownFormats.Avif];

    private readonly DialogService _dialogs;
    private readonly Lazy<IMainWindowHost> _host;
    private readonly ImageEditorService _editors;
    private readonly IToolLocator _tools;

    public ToolsViewModel(DialogService dialogs, Lazy<IMainWindowHost> host, ImageEditorService editors, IToolLocator tools)
    {
        _dialogs = dialogs;
        _host = host;
        _editors = editors;
        _tools = tools;
        PdfTools = BuildPdfTools();
    }

    public Action<Page>? Navigate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Subtitle), nameof(Cards), nameof(IsPdf))]
    private ToolsMode _mode = ToolsMode.Pdf;

    partial void OnModeChanged(ToolsMode value)
    {
        if (value == ToolsMode.Pdf)
        {
            PdfTools = BuildPdfTools(); // Ghostscript may have been installed meanwhile
            OnPropertyChanged(nameof(Cards));
        }
        InspectResult = null;
    }

    public bool IsPdf => Mode == ToolsMode.Pdf;
    public string Title => IsPdf ? "PDF Tools" : "Image Tools";
    public string Subtitle => IsPdf
        ? "Everything runs on this PC. OCR is not included."
        : "Crop and Add background open a small floating editor. Every tool saves a new file; originals are kept.";
    public IReadOnlyList<ToolCard> Cards => IsPdf ? PdfTools : ImageTools;

    public IReadOnlyList<ToolCard> PdfTools { get; private set; }

    private IReadOnlyList<ToolCard> BuildPdfTools()
    {
        var hasGhostscript = _tools.Find(ExternalTool.Ghostscript) is not null;
        return
        [
            new("Merge", "Combine several PDFs into one, in the order you set.", "M8 4h8v6H8zM8 14h8v6H8zM12 10v4", "Pdf",
                ToolAction.Convert, PdfToolsConverter.MergeKey, [KnownFormats.Pdf]),
            new("Split", "Save every page as its own PDF.", "M7 3h10v18H7zM3 12h18", "Pdf",
                ToolAction.Convert, PdfToolsConverter.SplitKey, [KnownFormats.Pdf]),
            new("Extract pages", "Copy selected pages (e.g. 1-3, 7) into a new PDF.", "M7 3h10v18H7zM10 9h4M10 13h4", "Pdf",
                ToolAction.Convert, PdfToolsConverter.ExtractKey, [KnownFormats.Pdf]),
            new("Rotate", "Rotate all pages, or the pages you list, by 90°, 180° or 270°.", "M4 12a8 8 0 1 0 2.3-5.7L4 9M4 4v5h5", "Pdf",
                ToolAction.Convert, PdfToolsConverter.RotateKey, [KnownFormats.Pdf]),
            new("Images → PDF", "JPG, PNG, WebP and more into a single document.", "M4 5h16v14H4zM4 16l5-5 4 4 3-3 4 4", "Pdf",
                ToolAction.Convert, PdfToolsConverter.CombineImagesKey, Images),
            new("PDF → Images", "Render pages to PNG, JPG or WebP at a chosen DPI.", "M7 3h7l5 5v13H7zM14 3v5h5", "Pdf",
                ToolAction.Convert, "png", [KnownFormats.Pdf]),
            new("Compress", "Lossless clean-up built in. Downsampling images needs Ghostscript.",
                "M12 3v6l3-3M12 9L9 6M12 21v-6l3 3M12 15l-3 3M4 12h16", "Pdf",
                ToolAction.Convert, PdfToolsConverter.CompressKey, [KnownFormats.Pdf])
            { Tag = hasGhostscript ? "Ready" : "Strong modes need Ghostscript", TagIsWarning = !hasGhostscript },
            new("Inspect", "Pages, PDF version, page size, producer and metadata.", "M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14zM21 21l-5-5", "Pdf",
                ToolAction.InspectPdf),
            new("Reorder", "Drag thumbnails to change page order.", "M8 6h12M8 12h12M8 18h12M4 6h.01M4 12h.01M4 18h.01", "Pdf",
                ToolAction.Planned) { Tag = "Planned" },
        ];
    }

    public IReadOnlyList<ToolCard> ImageTools { get; } =
    [
        new("Crop", "Free or 1:1, 16:9, 4:3, 3:4 with handles.", "M6 2v16h16M2 6h16v16", "Image", ToolAction.Crop, Accepts: Images, Multiple: false),
        new("Compress", "Smaller file in the same format (JPG, PNG, WebP, AVIF). Nothing is saved if it cannot get smaller.",
            "M12 3v6l3-3M12 9L9 6M12 21v-6l3 3M12 15l-3 3M4 12h16", "Image", ToolAction.Convert, ImageActions.ShrinkKey, [KnownFormats.Jpeg, KnownFormats.Png, KnownFormats.WebP, KnownFormats.Avif]),
        new("Remove metadata", "Strip EXIF, GPS and XMP from JPG and PNG without re-encoding the pixels.",
            "M12 2a7 7 0 0 0-7 7c0 5 7 13 7 13s7-8 7-13a7 7 0 0 0-7-7zM12 7a2 2 0 1 0 0 4 2 2 0 0 0 0-4z", "Image",
            ToolAction.Convert, ImageActions.StripKey, [KnownFormats.Jpeg, KnownFormats.Png]),
        new("Add background", "Gradients, padding, corners, shadow.", "M3 3h18v18H3zM7 7h10v10H7z", "Image",
            ToolAction.Background, Accepts: Images, Multiple: false),
        new("Edit", "Resize, rotate, flip, brightness, contrast.", "M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12M14 4v4M8 10v4M16 16v4", "Image",
            ToolAction.Planned) { Tag = "Planned · resize and rotate are in Convert options" },
        new("Annotate", "Text, arrows, shapes and highlights.", "M4 20l4-1 11-11-3-3L5 16zM14 6l3 3", "Image", ToolAction.Planned) { Tag = "Planned" },
        new("Redact", "Permanently flatten pixels. Not an overlay.", "M4 6h16v4H4zM4 14h10v4H4z", "Image", ToolAction.Planned) { Tag = "Planned" },
    ];

    public IReadOnlyList<ToolCard> ArchiveTools { get; } =
    [
        new("Create ZIP", "Compress files into a .zip archive.", "M7 3h10v18H7zM12 3v2M12 7v2M12 11v2", "Archive", ToolAction.Convert, "zip", []),
        new("Create 7z", "Compress files into a .7z archive (needs 7-Zip).", "M7 3h10v18H7zM12 3v2M12 7v2M12 11v2", "Archive", ToolAction.Convert, "7z", []),
        new("Extract archive", "Safely extract ZIP or 7z into a new folder.", "M4 8h16v12H4zM4 8l2-4h12l2 4M12 11v6M9 14l3 3 3-3", "Archive",
            ToolAction.Convert, ArchiveConverter.ExtractKey, [KnownFormats.Zip, KnownFormats.SevenZip]),
        new("Verify archive", "Check every entry's checksum without extracting.", "M4 8h16v12H4zM9 14l2 2 4-4", "Archive", ToolAction.TestArchive),
    ];

    [ObservableProperty] private string? _inspectTitle;
    [ObservableProperty] private string? _inspectResult;
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task Run(ToolCard card)
    {
        switch (card.Action)
        {
            case ToolAction.InspectPdf:
                await InspectPdfAsync();
                return;
            case ToolAction.TestArchive:
                await TestArchiveAsync();
                return;
            case ToolAction.Planned:
                return;
        }

        var files = card.Accepts is not { Count: > 0 }
            ? _dialogs.PickFiles(card.Title, (FormatCategory?)null, card.Multiple)
            : _dialogs.PickFiles(card.Title, card.Accepts, card.Multiple);
        if (files.Count == 0)
        {
            return;
        }
        switch (card.Action)
        {
            case ToolAction.Crop:
                _editors.OpenCrop(files[0]);
                break;
            case ToolAction.Background:
                _editors.OpenBackground(files[0]);
                break;
            default:
                _host.Value.ShowConvert(files, card.TargetKey);
                break;
        }
    }

    private async Task InspectPdfAsync()
    {
        var file = _dialogs.PickFiles("Choose a PDF", [KnownFormats.Pdf], multiple: false).FirstOrDefault();
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

    private async Task TestArchiveAsync()
    {
        var file = _dialogs.PickFiles("Choose an archive to verify", [KnownFormats.Zip, KnownFormats.SevenZip], multiple: false).FirstOrDefault();
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

    [RelayCommand]
    private void CloseInspect() => InspectResult = null;

    private static void Append(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            sb.AppendLine($"{label}: {value}");
        }
    }
}
