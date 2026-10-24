# Customizing the components: slots

The parts of the `ReportDesigner`, `FieldList`, `FilterBar`, `FilterEditor` and `DashboardView` are **slots**: named places
where an application adds its own content (before or after the default content), replaces it, or hides it.

```xml
<bi:ReportDesigner Context="{Binding Context}">
    <bi:Slots.Items>
        <!-- Under the preview -->
        <bi:Slot Name="{x:Static bi:ReportDesignerSlots.Preview}">
            <Button Content="Publish" Command="{Binding DataContext.PublishCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
        </bi:Slot>
        <bi:Slot Name="{x:Static bi:ReportDesignerSlots.Title}" Mode="Before"><TextBlock Text="Title:" /></bi:Slot>
        <bi:Slot Name="{x:Static bi:ReportDesignerSlots.FiltersShelf}" Mode="Hide" />
    </bi:Slots.Items>
</bi:ReportDesigner>
```

```csharp
Slots.Add(designer, ReportDesignerSlots.Header, exportButton);              // after the default content
Slots.Add(designer, ReportDesignerSlots.Fields, help, SlotMode.Before);      // above the field list
Slots.Add(filterBar, FilterBarSlots.AddButton, myAddButton, SlotMode.Replace);
Slots.Hide(designer, ReportDesignerSlots.FieldPanel);
```

- **Names**: `ReportDesignerSlots` (`FieldPanel`, `Fields`, `Header`, `Title`, `VisualTypes`, `Shelves`, `AxisShelf`,
  `LegendShelf`, `ValuesShelf`, `FiltersShelf`, `Preview`, `Properties`), `FieldListSlots` (`Model`, `Search`, `Dimensions`,
  `Measures`, `AddMeasureButton`), `FilterBarSlots` (`Filters`, `Label`, `Slicer`, `AddButton`, `Selection`),
  `FilterEditorSlots` (`Values`, `Period`) and `DashboardSlots` (`Body`, `ItemContent`, `EmptyItemChoices`, `ItemToolbar`). Slots can be
  nested: `Header` contains `Title` and `VisualTypes`. The band menu of a dashboard is customized through
  `BandMenuBehavior.MenuOpening`.
- **Modes**: `After` (default), `Before`, `Replace`, `Hide`. Before and after are left and right of the default content,
  or above and below it in a vertical slot (`Fields`, `Shelves`, `Preview`); several slots with the same name are shown
  in declaration order, and the last `Replace` or `Hide` wins.
- **Data context**: the content takes the data context of the place it is shown in: the `DesignerViewModel` in the
  designer, the application's in the filter bar and the field list, the `SlicerViewModel` in `FilterBarSlots.Slicer`, the
  `FilterItemViewModel` in the filter editor (shown once per filter, so its slots take a template), the `DashboardItem`
  in the dashboard item slots (commands such as `DashboardCommands.RemoveItem` act on the item containing them).
- **Repeated slots**: an element can only be shown once. A slot shown several times (`FilterBarSlots.Slicer`, once per
  global filter, the dashboard item slots, once per item, or slots declared for several components) takes a `ContentTemplate` (`Slots.AddTemplate(...)` in
  code), applied to the data context of each place.
- **Inheritance**: `Slots.Items` is inherited, so slots declared on a window or a panel apply to every component in it;
  the nearest declaration replaces the others. The designer opened to edit a dashboard visual takes the slots of the
  dashboard.
- **Custom templates**: the components are lookless controls. Their default templates (`Themes/*.xaml` of `EasyBI.Wpf`)
  are built from `SlotHost`s (`<bi:SlotHost Slot="..." Orientation="Vertical">` with the default content inside), so a
  template of the application keeps the slots it hosts. A template works with the components' read-only properties
  and commands: `ReportDesigner.ViewModel` (the template's data context); `FieldList.ModelOptions`, `Dimensions`,
  `Measures`, `CanAddMeasure` and `AddMeasureCommand`, `EditMeasureCommand`, `DeleteMeasureCommand`,
  `ResetMeasureCommand` (with `ModelField.IsEditable`, `IsReportMeasure` and `IsRedefined`);
  `FilterBar.Slicers`, `SelectionDescription` and `AddFilterCommand`, `RemoveFilterCommand` (on the slicer of its
  data context), `ClearSelectionCommand`; `DashboardView.Panel` and `DashboardCommands`.
