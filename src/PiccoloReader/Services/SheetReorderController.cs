using System.Collections.ObjectModel;
using PiccoloReader.Core.Data.Models;
#if ANDROID
using Android.Views;
using AndroidX.RecyclerView.Widget;
#endif

namespace PiccoloReader.Services;

/// <summary>
/// Drag-to-reorder for the sheet list. On Android it attaches a native
/// ItemTouchHelper to the CollectionView's RecyclerView with long-press drag
/// turned off; the drag handle of a row starts it from its own long press, so
/// the row's long-press menu is untouched. Moves are applied to the bound
/// ObservableCollection, which MAUI's adapter turns into animated item moves.
/// Other platforms do nothing (the handles are hidden there).
/// </summary>
public sealed class SheetReorderController
{
#if ANDROID
    private readonly CollectionView _list;
    private readonly ObservableCollection<Sheet> _items;
    private readonly Func<bool> _canReorder;
    private readonly Func<Task> _onDropped;
    private ItemTouchHelper? _helper;
    private RecyclerView? _recyclerView;

    public SheetReorderController(CollectionView list, ObservableCollection<Sheet> items, Func<bool> canReorder, Func<Task> onDropped)
    {
        _list = list;
        _items = items;
        _canReorder = canReorder;
        _onDropped = onDropped;
        _list.HandlerChanged += (_, _) => Attach();
        Attach();
    }

    private void Attach()
    {
        _helper?.AttachToRecyclerView(null);
        _helper = null;
        _recyclerView = _list.Handler?.PlatformView as RecyclerView;
        if (_recyclerView is null)
        {
            return;
        }

        _helper = new ItemTouchHelper(new DragCallback(this));
        _helper.AttachToRecyclerView(_recyclerView);
    }

    /// <summary>Wires a row's drag handle: its long press starts the drag.</summary>
    public void WireHandle(VisualElement handle)
    {
        if (handle.Handler?.PlatformView is not Android.Views.View view)
        {
            return;
        }

        view.LongClickable = true;
        view.LongClick -= OnHandleLongClick;
        view.LongClick += OnHandleLongClick;
    }

    private void OnHandleLongClick(object? sender, Android.Views.View.LongClickEventArgs e)
    {
        if (sender is not Android.Views.View view || _helper is null || _recyclerView is null || !_canReorder())
        {
            return;
        }

        var holder = _recyclerView.FindContainingViewHolder(view);
        if (holder is not null && ToItemIndex(holder) >= 0)
        {
            view.Parent?.RequestDisallowInterceptTouchEvent(true);
            _helper.StartDrag(holder);
        }
    }

    // The header is adapter position 0; -1 when the holder is not a sheet row.
    private int ToItemIndex(RecyclerView.ViewHolder holder)
    {
        var offset = _list.Header is null ? 0 : 1;
        var index = holder.BindingAdapterPosition - offset;
        return index >= 0 && index < _items.Count ? index : -1;
    }

    private sealed class DragCallback : ItemTouchHelper.Callback
    {
        private readonly SheetReorderController _owner;
        private List<int>? _orderAtStart;

        public DragCallback(SheetReorderController owner) => _owner = owner;

        public override bool IsLongPressDragEnabled => false;

        public override bool IsItemViewSwipeEnabled => false;

        public override int GetMovementFlags(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder)
        {
            var draggable = _owner._canReorder() && _owner.ToItemIndex(viewHolder) >= 0;
            return MakeMovementFlags(draggable ? ItemTouchHelper.Up | ItemTouchHelper.Down : 0, 0);
        }

        public override bool OnMove(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder, RecyclerView.ViewHolder target)
        {
            var from = _owner.ToItemIndex(viewHolder);
            var to = _owner.ToItemIndex(target);
            if (from < 0 || to < 0)
            {
                return false;
            }

            if (from != to)
            {
                _owner._items.Move(from, to);
            }

            return true;
        }

        public override void OnSwiped(RecyclerView.ViewHolder viewHolder, int direction)
        {
        }

        public override void OnSelectedChanged(RecyclerView.ViewHolder? viewHolder, int actionState)
        {
            base.OnSelectedChanged(viewHolder, actionState);
            if (viewHolder is not null && actionState == ItemTouchHelper.ActionStateDrag)
            {
                _orderAtStart = _owner._items.Select(s => s.Id).ToList();
                viewHolder.ItemView.Alpha = 0.85f;
            }
        }

        public override void ClearView(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder)
        {
            base.ClearView(recyclerView, viewHolder);
            viewHolder.ItemView.Alpha = 1f;

            var before = _orderAtStart;
            _orderAtStart = null;
            if (before is not null && !before.SequenceEqual(_owner._items.Select(s => s.Id)))
            {
                _ = _owner._onDropped();
            }
        }
    }
#else
    public SheetReorderController(CollectionView list, ObservableCollection<Sheet> items, Func<bool> canReorder, Func<Task> onDropped)
    {
    }

    public void WireHandle(VisualElement handle)
    {
    }
#endif
}
