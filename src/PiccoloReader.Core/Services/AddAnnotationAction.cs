using System.Collections.ObjectModel;
using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

public class AddAnnotationAction : IUndoableAction
{
    private readonly AnnotationService _annotationService;
    private readonly ObservableCollection<Annotation> _pageAnnotations;
    private readonly Annotation _annotation;

    public AddAnnotationAction(AnnotationService annotationService, ObservableCollection<Annotation> pageAnnotations, Annotation annotation)
    {
        _annotationService = annotationService;
        _pageAnnotations = pageAnnotations;
        _annotation = annotation;
    }

    public async Task UndoAsync()
    {
        await _annotationService.DeleteAnnotationAsync(_annotation);
        _pageAnnotations.Remove(_annotation);
    }

    public async Task RedoAsync()
    {
        await _annotationService.InsertAnnotationAsync(_annotation);
        _pageAnnotations.Add(_annotation);
    }
}
