# Tooltips

Hovering a bar, a point, a slice or a step shows an EasyBI tooltip, the same with every connector: the category, then
each hovered series with its color and value. The end user sets it up per chart in the designer:

- the **Tooltip** well takes measures shown in the tooltip only (they are queried with the chart, but are no series);
- the **Tooltips** section of the properties shows or hides the tooltips, the category, the series' names and each
  value's share of its series' total (%), and takes a **custom text** replacing the content, with fields between braces:
  `{category}`, `{series}`, `{value}`, `{percent}` and any measure of the chart or of the well, by name or caption
  (`"{category}: {value} for {Quantity} sales"`; an unknown field stays as typed).

The settings are saved with the visual (`VisualDefinition.Tooltip`, a `TooltipSettings`, omitted when it has none).

For the developer, the content is a `TooltipContent` (a `Title`, `Items` of label, value and color, or a `Text`):

- **Change it** for every chart, before it opens: `EasyBIContext.TooltipOpening` gives the visual, the hovered category
  and series and EasyBI's content; replace `Content`, or set it to null to show nothing.

  ```csharp
  context.TooltipOpening += (_, e) =>
  {
      if (e.Visual.Model == "sales" && e.Content is { } content)
          e.Content = content with { Items = [.. content.Items, new TooltipItem("Target", Targets.For(content.Title))] };
  };
  ```

- **Restyle it**: the frame is a standard `ToolTip` (it takes the application's tooltip style) and the content is drawn
  by a `DataTemplate` under `ChartTooltips.TemplateKey`, which an application replaces in its resources:

  ```xml
  <DataTemplate x:Key="{x:Static renderers:ChartTooltips.TemplateKey}">
      <StackPanel>
          <TextBlock Text="{Binding Title}" FontWeight="Bold" />
          <ItemsControl ItemsSource="{Binding Items}" DisplayMemberPath="Value" />
      </StackPanel>
  </DataTemplate>
  ```

- **Build it** without a view: `Tooltips.Build(request, category, series)` (EasyBI.Core) gives the default content of a
  `RenderRequest`.
