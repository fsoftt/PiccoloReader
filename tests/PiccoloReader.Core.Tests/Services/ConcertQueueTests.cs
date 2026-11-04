using PiccoloReader.Core.Services.Concert;

namespace PiccoloReader.Core.Tests.Services;

public class ConcertQueueTests
{
    private static ConcertQueue Queue(params int[] ids)
    {
        var queue = new ConcertQueue();
        queue.SetQueue(ids);
        return queue;
    }

    [Fact]
    public void NextAfter_ReturnsFollowingId() => Assert.Equal(20, Queue(10, 20, 30).NextAfter(10));

    [Fact]
    public void PreviousBefore_ReturnsPrecedingId() => Assert.Equal(20, Queue(10, 20, 30).PreviousBefore(30));

    [Fact]
    public void NextAfter_LastItem_ReturnsNull() => Assert.Null(Queue(10, 20, 30).NextAfter(30));

    [Fact]
    public void PreviousBefore_FirstItem_ReturnsNull() => Assert.Null(Queue(10, 20, 30).PreviousBefore(10));

    [Fact]
    public void UnknownId_ReturnsNullBothWays()
    {
        var queue = Queue(10, 20);

        Assert.Null(queue.NextAfter(99));
        Assert.Null(queue.PreviousBefore(99));
    }

    [Fact]
    public void EmptyQueue_ReturnsNull()
    {
        var queue = new ConcertQueue();

        Assert.Null(queue.NextAfter(1));
        Assert.Null(queue.PreviousBefore(1));
    }

    [Fact]
    public void SingleItem_HasNoNeighbours()
    {
        var queue = Queue(5);

        Assert.Null(queue.NextAfter(5));
        Assert.Null(queue.PreviousBefore(5));
    }

    [Fact]
    public void SetQueue_ReplacesPreviousAndCopiesInput()
    {
        var source = new List<int> { 1, 2, 3 };
        var queue = new ConcertQueue();
        queue.SetQueue(new[] { 7, 8 });

        queue.SetQueue(source);
        source.Clear();

        Assert.Equal(new[] { 1, 2, 3 }, queue.SheetIds);
        Assert.Equal(2, queue.NextAfter(1));
    }

    [Fact]
    public void Clear_EmptiesQueue()
    {
        var queue = Queue(1, 2);

        queue.Clear();

        Assert.Empty(queue.SheetIds);
        Assert.Null(queue.NextAfter(1));
    }
}
