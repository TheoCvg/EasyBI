# Drag and drop

The designer's drag and drop is built on reusable attached properties (namespace `EasyBI.Wpf.DragAndDrop`,
XAML prefix `bi:`), usable on any `ItemsControl` of an application. Being attached properties, they can be set
from a style:

```xml
<!-- Items can be dragged: a preview follows the pointer, a moved item is dimmed -->
<ListBox ItemsSource="{Binding Fields}" bi:DragSource.IsEnabled="True" bi:DragSource.AllowedEffects="Copy" />

<!-- Drops are decided by an IDropHandler (usually the view-model); an insertion line shows where the item goes -->
<ItemsControl ItemsSource="{Binding Columns}" bi:DropTarget.Handler="{Binding}" bi:DropTarget.Key="Columns" />
```

```csharp
public sealed class MyViewModel : IDropHandler
{
    public DragDropEffects DragOver(DropInfo drop) => drop.Item is Field ? DragDropEffects.Copy : DragDropEffects.None;
    public void Drop(DropInfo drop) => Columns.Insert(drop.Index, ((Field)drop.Item!).ToColumn());
}
```

- `DropInfo` gives the dragged `Item`, the target's `Key` (one handler for several targets), the insertion
  `Index` among the target's items and the raw `Data` for drags coming from elsewhere.
- `DropTarget.IsDragOver` is true while an accepted drag is over the target (style triggers);
  `DropTarget.Orientation` switches the insertion rules to a vertical stack.
- A handler may explain a refusal (`IDropHandler.RefusalReason`, optional): the reason is shown under the target and
  set on it as `DropTarget.Refusal` while the drag is over it.
- A scrollable target, or its scroll viewer, scrolls when the pointer nears its edges (`AutoScroller`, also usable
  on its own); `InsertionIndex` holds the pure insertion rules.
- Style keys `DragSource.PreviewStyleKey` (a `ContentControl`; `DragSource.PreviewTemplate` sets the content),
  `DropTarget.InsertionLineStyleKey` (a `Border`) and `DropTarget.RefusalStyleKey` (a `ContentControl` holding the reason).

In the designer, fields are dropped between chips, chips are reordered within a shelf, and dimensions move between
Axis and Legend keeping their granularity. A refused field says why (`DesignerViewModel.RefusalReason`): only measures
in Values, only dimensions elsewhere, a field already on the shelf, a full axis, a field of another model.
