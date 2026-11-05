using System.Collections.ObjectModel;
using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

// One undo step that swaps some annotations for others: a drawn stroke (or
// two) for the figure it was recognized as. It is created after the swap has
// happened, so undo puts the removed annotations back and drops the added
// ones; redo does the reverse. The removed ones may never have been saved
// (a stroke that was recognized before it was stored): undo then inserts them.
public class ReplaceAnnotationsAction : IUndoableAction
{
    private readonly AnnotationService _annotationService;
    private readonly ObservableCollection<Annotation> _pageAnnotations;
    private readonly IReadOnlyList<Annotation> _removed;
    private readonly IReadOnlyList<Annotation> _added;

    public ReplaceAnnotationsAction(
        AnnotationService annotationService,
        ObservableCollection<Annotation> pageAnnotations,
        IReadOnlyList<Annotation> removed,
        IReadOnlyList<Annotation> added)
    {
        _annotationService = annotationService;
        _pageAnnotations = pageAnnotations;
        _removed = removed;
        _added = added;
    }

    public async Task UndoAsync()
    {
        foreach (var annotation in _added)
        {
            await _annotationService.DeleteAnnotationAsync(annotation);
            _pageAnnotations.Remove(annotation);
        }

        foreach (var annotation in _removed)
        {
            await _annotationService.InsertAnnotationAsync(annotation);
            _pageAnnotations.Add(annotation);
        }
    }

    public async Task RedoAsync()
    {
        foreach (var annotation in _removed)
        {
            await _annotationService.DeleteAnnotationAsync(annotation);
            _pageAnnotations.Remove(annotation);
        }

        foreach (var annotation in _added)
        {
            await _annotationService.InsertAnnotationAsync(annotation);
            _pageAnnotations.Add(annotation);
        }
    }
}
