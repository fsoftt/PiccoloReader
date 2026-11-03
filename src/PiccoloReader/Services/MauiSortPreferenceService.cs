using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Services;

public class MauiSortPreferenceService : ISortPreferenceService
{
    private const string FieldKey = "SheetSortField";
    private const string DirectionKey = "SheetSortDirection";

    private readonly object _gate = new();

    // Cached; the constructor loads it off the UI thread.
    private (SortField Field, SortDirection Direction)? _value;

    public MauiSortPreferenceService()
    {
        _ = Task.Run(Load);
    }

    public SortField SheetSortField => Load().Field;

    public SortDirection SheetSortDirection => Load().Direction;

    public void Save(SortField field, SortDirection direction)
    {
        lock (_gate)
        {
            _value = (field, direction);
        }

        // Preferences writes are applied asynchronously by the platform.
        Preferences.Default.Set(FieldKey, (int)field);
        Preferences.Default.Set(DirectionKey, (int)direction);
    }

    private (SortField Field, SortDirection Direction) Load()
    {
        lock (_gate)
        {
            if (_value is { } cached)
            {
                return cached;
            }

            var field = Preferences.Default.Get(FieldKey, (int)SortField.Name);
            var direction = Preferences.Default.Get(DirectionKey, (int)SortDirection.Ascending);
            var loaded = (
                Enum.IsDefined(typeof(SortField), field) ? (SortField)field : SortField.Name,
                Enum.IsDefined(typeof(SortDirection), direction) ? (SortDirection)direction : SortDirection.Ascending);
            _value = loaded;
            return loaded;
        }
    }
}
