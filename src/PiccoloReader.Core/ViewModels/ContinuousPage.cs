using CommunityToolkit.Mvvm.ComponentModel;
using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.ViewModels;

// One page in the continuous (vertical scroll) reading mode. Every page of
// the sheet gets one of these up front, but ImageBytes/Annotations are only
// filled in while the page is on or near the screen and dropped again once
// it scrolls far away, so long sheets don't hold every rendered page in
// memory.
public partial class ContinuousPage : ObservableObject
{
    public ContinuousPage(int pageIndex, double aspectRatio)
    {
        PageIndex = pageIndex;
        _aspectRatio = aspectRatio;
    }

    public int PageIndex { get; }

    [ObservableProperty]
    private byte[]? _imageBytes;

    // Height divided by width of what is SHOWN (the cropped part, when the
    // page has a crop). Starts as an estimate (the screen's own ratio) and is
    // corrected from the rendered image once it arrives.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullAspectRatio))]
    private double _aspectRatio;

    // The visible part of the page, normalized to the full page. Annotations
    // stay normalized to the full page and are mapped through it.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullAspectRatio))]
    private PageCrop _crop = PageCrop.Full;

    // Height divided by width of the whole page.
    public double FullAspectRatio => Crop.FullAspectRatio(AspectRatio);

    [ObservableProperty]
    private IReadOnlyList<Annotation> _annotations = Array.Empty<Annotation>();

    internal bool IsLoadRequested { get; set; }
}
