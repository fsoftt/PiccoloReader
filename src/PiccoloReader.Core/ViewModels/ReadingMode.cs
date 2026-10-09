namespace PiccoloReader.Core.ViewModels;

public enum ReadingMode
{
    // One page at a time, swipe left/right or tap the side edges.
    Horizontal,

    // One page at a time, swipe up/down or tap the top/bottom edges - the
    // whole page is replaced by the next one.
    VerticalPaged,

    // All pages stacked in a vertical list that scrolls freely, so the end
    // of one page stays visible while the next one scrolls in.
    VerticalContinuous
}
