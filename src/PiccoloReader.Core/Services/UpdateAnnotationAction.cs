using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

public class UpdateAnnotationAction : IUndoableAction
{
    private readonly AnnotationService _annotationService;
    private readonly Annotation _annotation;
    private readonly (double X, double Y, double Width, double Height) _before;
    private readonly (double X, double Y, double Width, double Height) _after;
    private readonly string? _beforeText;
    private readonly string? _afterText;

    // beforeText / afterText are only given when the step also changes a text
    // annotation's content.
    public UpdateAnnotationAction(
        AnnotationService annotationService,
        Annotation annotation,
        (double X, double Y, double Width, double Height) before,
        (double X, double Y, double Width, double Height) after,
        string? beforeText = null,
        string? afterText = null)
    {
        _annotationService = annotationService;
        _annotation = annotation;
        _before = before;
        _after = after;
        _beforeText = beforeText;
        _afterText = afterText;
    }

    public Task UndoAsync() => ApplyAsync(_before, _beforeText);

    public Task RedoAsync() => ApplyAsync(_after, _afterText);

    private Task ApplyAsync((double X, double Y, double Width, double Height) values, string? text)
    {
        _annotation.X = values.X;
        _annotation.Y = values.Y;
        _annotation.Width = values.Width;
        _annotation.Height = values.Height;
        if (text is not null)
        {
            _annotation.Text = text;
        }

        return _annotationService.UpdateAnnotationAsync(_annotation);
    }
}
