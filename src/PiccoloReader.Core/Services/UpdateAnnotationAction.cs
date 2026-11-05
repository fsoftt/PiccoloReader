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
    private readonly string? _beforeColor;
    private readonly string? _afterColor;

    // beforeText / afterText are only given when the step also changes a text
    // annotation's content.
    public UpdateAnnotationAction(
        AnnotationService annotationService,
        Annotation annotation,
        (double X, double Y, double Width, double Height) before,
        (double X, double Y, double Width, double Height) after,
        string? beforeText = null,
        string? afterText = null,
        string? beforeColor = null,
        string? afterColor = null)
    {
        _annotationService = annotationService;
        _annotation = annotation;
        _before = before;
        _after = after;
        _beforeText = beforeText;
        _afterText = afterText;
        _beforeColor = beforeColor;
        _afterColor = afterColor;
    }

    public Task UndoAsync() => ApplyAsync(_before, _beforeText, _beforeColor);

    public Task RedoAsync() => ApplyAsync(_after, _afterText, _afterColor);

    private Task ApplyAsync((double X, double Y, double Width, double Height) values, string? text, string? color)
    {
        _annotation.X = values.X;
        _annotation.Y = values.Y;
        _annotation.Width = values.Width;
        _annotation.Height = values.Height;
        if (color is not null)
        {
            _annotation.ColorHex = color;
        }

        if (text is not null)
        {
            _annotation.Text = text;
        }

        return _annotationService.UpdateAnnotationAsync(_annotation);
    }
}
