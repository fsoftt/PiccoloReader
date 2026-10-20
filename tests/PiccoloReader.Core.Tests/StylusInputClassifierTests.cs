using PiccoloReader.Core.Services.Stylus;

namespace PiccoloReader.Core.Tests;

public class StylusInputClassifierTests
{
    [Theory]
    [InlineData(PointerToolKind.Stylus, false, false, StylusRole.Draw)]
    [InlineData(PointerToolKind.Stylus, true, false, StylusRole.Erase)]
    [InlineData(PointerToolKind.Stylus, false, true, StylusRole.Erase)]
    [InlineData(PointerToolKind.Stylus, true, true, StylusRole.Erase)]
    [InlineData(PointerToolKind.Eraser, false, false, StylusRole.Erase)]
    [InlineData(PointerToolKind.Eraser, true, false, StylusRole.Erase)]
    [InlineData(PointerToolKind.Finger, false, false, StylusRole.Finger)]
    [InlineData(PointerToolKind.Finger, true, false, StylusRole.Finger)]
    [InlineData(PointerToolKind.Mouse, true, false, StylusRole.Finger)]
    [InlineData(PointerToolKind.Unknown, false, false, StylusRole.Finger)]
    public void Classify_ReturnsExpectedRole(PointerToolKind tool, bool primary, bool secondary, StylusRole expected)
    {
        Assert.Equal(expected, StylusInputClassifier.Classify(tool, primary, secondary));
    }
}
