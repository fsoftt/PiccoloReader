using System.Collections.ObjectModel;
using System.Collections.Specialized;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.ViewModels;

public class ObservableCollectionSyncTests
{
    private static (ObservableCollection<string> Target, List<NotifyCollectionChangedAction> Actions) SyncFrom(
        IEnumerable<string> initial,
        IReadOnlyList<string> desired)
    {
        var target = new ObservableCollection<string>(initial);
        var actions = new List<NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, e) => actions.Add(e.Action);

        target.SyncWith(desired);

        return (target, actions);
    }

    [Fact]
    public void EmptyToItems_AddsAllInOrder()
    {
        var (target, actions) = SyncFrom([], ["a", "b", "c"]);

        Assert.Equal(["a", "b", "c"], target);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    [Fact]
    public void ItemsToEmpty_RemovesAll()
    {
        var (target, actions) = SyncFrom(["a", "b", "c"], []);

        Assert.Empty(target);
        Assert.NotEmpty(actions);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    [Fact]
    public void Filtering_RemovesOnlyMissingItems()
    {
        var (target, actions) = SyncFrom(["a", "b", "c", "d"], ["a", "c"]);

        Assert.Equal(["a", "c"], target);
        Assert.All(actions, a => Assert.Equal(NotifyCollectionChangedAction.Remove, a));
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    [Fact]
    public void InsertInMiddle_PlacesNewItemAtCorrectIndex()
    {
        var (target, actions) = SyncFrom(["a", "c"], ["a", "b", "c"]);

        Assert.Equal(["a", "b", "c"], target);
        Assert.Equal([NotifyCollectionChangedAction.Add], actions);
    }

    [Fact]
    public void Reorder_MovesItemsIntoTargetOrder()
    {
        var (target, actions) = SyncFrom(["a", "b", "c"], ["c", "a", "b"]);

        Assert.Equal(["c", "a", "b"], target);
        Assert.Contains(NotifyCollectionChangedAction.Move, actions);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    [Fact]
    public void IdenticalLists_RaiseNoCollectionChangedEvents()
    {
        var (target, actions) = SyncFrom(["a", "b", "c"], ["a", "b", "c"]);

        Assert.Equal(["a", "b", "c"], target);
        Assert.Empty(actions);
    }

    [Fact]
    public void Sync_NeverRaisesReset_ForMixedChanges()
    {
        var (target, actions) = SyncFrom(
            ["a", "b", "c", "d", "e"],
            ["e", "x", "a", "c", "y"]);

        Assert.Equal(["e", "x", "a", "c", "y"], target);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }
}
