using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

public class UpdateAnnotationAction : IUndoableAction
{
    private readonly AnnotationService _annotationService;
    private readonly Annotation _annotation;
    private readonly (double X, double Y, double Width, double Height) _before;
    private readonly (double X, double Y, double Width, double Height) _after;

    public UpdateAnnotationAction(
        AnnotationService annotationService,
        Annotation annotation,
        (double X, double Y, double Width, double Height) before,
        (double X, double Y, double Width, double Height) after)
    {
        _annotationService = annotationService;
        _annotation = annotation;
        _before = before;
        _after = after;
    }

    public Task UndoAsync() => ApplyAsync(_before);

    public Task RedoAsync() => ApplyAsync(_after);

    private Task ApplyAsync((double X, double Y, double Width, double Height) values)
    {
        _annotation.X = values.X;
        _annotation.Y = values.Y;
        _annotation.Width = values.Width;
        _annotation.Height = values.Height;
        return _annotationService.UpdateAnnotationAsync(_annotation);
    }
}
