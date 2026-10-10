using System.Collections.ObjectModel;

namespace PiccoloReader.Core.ViewModels;

public static class ObservableCollectionSync
{
    // Makes `target` match `desired` with the minimum remove/insert changes
    // instead of Clear()+Add(). A Reset notification makes the Android
    // CollectionView rebind its Header, and the search Entry lives in that
    // header, so every keystroke used to steal its focus (and the keyboard).
    public static void SyncWith<T>(this ObservableCollection<T> target, IReadOnlyList<T> desired)
    {
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && EqualityComparer<T>.Default.Equals(target[i], desired[i]))
            {
                continue;
            }

            var existing = target.IndexOf(desired[i]);
            if (existing >= 0)
            {
                target.Move(existing, i);
            }
            else
            {
                target.Insert(i, desired[i]);
            }
        }
    }
}
