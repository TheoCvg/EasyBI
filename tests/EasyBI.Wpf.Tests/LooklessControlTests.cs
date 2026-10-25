using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Dashboard;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

/// <summary>The components are lookless: an application replaces their template and keeps their behavior.</summary>
public class LooklessControlTests
{
    private sealed record Row(string Region, decimal Amount);

    private static EasyBIContext Context() => EasyBIContext.Create().AddModel(
        SemanticModel.For(new[] { new Row("Nord", 1m) }, "sales").Dimension(r => r.Region, "Region").Measure(r => r.Amount, "Amount"));

    private static ControlTemplate Template<T>(string content) => (ControlTemplate)XamlReader.Parse(
        $$"""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:bi="clr-namespace:{{typeof(T).Namespace}};assembly=EasyBI.Wpf"
                         TargetType="{x:Type bi:{{typeof(T).Name}}}">
            {{content}}
        </ControlTemplate>
        """);

    private static void Layout(UIElement element)
    {
        element.Hosted().Measure(new Size(1000, 700));
        element.Arrange(new Rect(0, 0, 1000, 700));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static string[] SlotNames(Type type) => type.GetFields().Select(f => (string)f.GetValue(null)!).ToArray();

    [Fact]
    public void Every_field_list_and_filter_editor_slot_has_a_host() => Sta.Run(() =>
    {
        var fields = new FieldList { Context = Context() };
        var editor = new FilterEditor();
        Layout(fields);
        Layout(editor);

        foreach (var (type, component) in new (Type, Control)[] { (typeof(FieldListSlots), fields), (typeof(FilterEditorSlots), editor) })
        {
            var hosts = Descendants(component).OfType<SlotHost>().Select(h => h.Slot).ToArray();
            Assert.All(SlotNames(type), name => Assert.Contains(name, hosts));
        }
    });

    [Fact]
    public void An_application_template_lists_the_filter_bar_slicers_and_removes_them_with_its_command() => Sta.Run(() =>
    {
        var filters = new FilterContext();
        filters.SetFilter("sales", new FilterSpec("Region", FilterOperator.In, ["Nord"]));
        var bar = new FilterBar
        {
            Context = Context(),
            FilterContext = filters,
            Template = Template<FilterBar>(
                """
                <ItemsControl ItemsSource="{Binding Slicers, RelativeSource={RelativeSource TemplatedParent}}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Button Content="{Binding Caption}" Command="{x:Static bi:FilterBar.RemoveFilterCommand}" />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                """),
        };
        Layout(bar);

        var remove = Descendants(bar).OfType<Button>().Single();
        Assert.Equal("Region", remove.Content);
        Assert.True(FilterBar.RemoveFilterCommand.CanExecute(null, remove));
        FilterBar.RemoveFilterCommand.Execute(null, remove);

        Assert.Empty(bar.Slicers);
        Assert.Empty(filters.Filters);
    });

    [Fact]
    public void Field_list_commands_are_available_once_there_is_a_model() => Sta.Run(() =>
    {
        var fields = new FieldList();
        Layout(fields);
        Assert.False(FieldList.AddMeasureCommand.CanExecute(null, fields));

        fields.Context = Context();

        Assert.True(fields.CanAddMeasure);
        Assert.True(FieldList.AddMeasureCommand.CanExecute(null, fields));
        Assert.Equal(["Region"], fields.Dimensions.Select(f => f.Caption));
        // A measure of the model is not a calculated measure: it cannot be edited or deleted
        Assert.False(FieldList.DeleteMeasureCommand.CanExecute(fields.Measures.Single(), fields));
    });

    [Fact]
    public void Designer_template_takes_the_view_model_as_data_context() => Sta.Run(() =>
    {
        var designer = new ReportDesigner { DataContext = new DesignerViewModel(Context()) };
        Slots.AddTemplate(designer, ReportDesignerSlots.Preview, (DataTemplate)XamlReader.Parse(
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <TextBlock Tag="probe" Text="{Binding Title}" />
            </DataTemplate>
            """));
        Layout(designer);

        var probe = Descendants(designer).OfType<TextBlock>().Single(t => Equals(t.Tag, "probe"));
        Assert.Same(designer.DataContext, probe.DataContext);
    });

    [Fact]
    public void A_dashboard_template_shows_the_panel_where_it_wants() => Sta.Run(() =>
    {
        var view = new DashboardView
        {
            Template = Template<DashboardView>(
                """
                <DockPanel>
                    <TextBlock DockPanel.Dock="Top" Text="header" Height="40" />
                    <AdornerDecorator>
                        <ContentPresenter Content="{Binding Panel, RelativeSource={RelativeSource TemplatedParent}}" />
                    </AdornerDecorator>
                </DockPanel>
                """),
        };
        view.Load(new DashboardDocument { Report = new ReportDefinition { Visuals = [new VisualDefinition { Id = "a", Type = VisualType.Text, Text = new TextContent { Text = "a" } }] } });
        Layout(view);

        Assert.Equal(40, view.Panel.TranslatePoint(new Point(), view).Y);
        Assert.Same(view, LogicalTreeHelper.GetParent(view.Panel)); // inherited values and resources come from the view
        Assert.Single(view.Items);
    });

    // No input after the context is set: the buttons must not wait for one to ask their commands again
    private static bool EnabledOnceThereIsAContext<T>(T component, Action<T, EasyBIContext> setContext, ICommand command) where T : Control
    {
        var window = new Window { Content = component, Width = 600, Height = 600, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            setContext(component, Context());
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            return Descendants(component).OfType<Button>().Single(b => b.Command == command).IsEnabled;
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void The_add_filter_button_is_enabled_once_there_is_a_context() => Sta.Run(() =>
        Assert.True(EnabledOnceThereIsAContext(new FilterBar { FilterContext = new FilterContext() }, (bar, context) => bar.Context = context, FilterBar.AddFilterCommand)));

    [Fact]
    public void The_add_measure_button_is_enabled_once_there_is_a_context() => Sta.Run(() =>
        Assert.True(EnabledOnceThereIsAContext(new FieldList(), (fields, context) => fields.Context = context, FieldList.AddMeasureCommand)));
}
