using System.Globalization;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

public class VariableTests
{
    private sealed record Row(string Region, string Channel, DateTime Day, decimal Amount);

    private static readonly Row[] Rows =
    [
        new("Nord", "Web", new(2024, 1, 10), 100m),
        new("Nord", "Shop", new(2024, 3, 5), 300m),
        new("Sud", "Web", new(2024, 2, 1), 200m),
        new("Est", "Shop", new(2024, 3, 20), 50m),
        new("Ouest", "Web", new(2024, 4, 2), 120m),
    ];

    private static QueryableSemanticModel<Row> Model() =>
        SemanticModel.For(Rows.AsQueryable(), "m")
            .Dimension(r => r.Region, "Région")
            .Dimension(r => r.Channel, "Canal")
            .Dimension(r => r.Day, "Jour")
            .Measure(r => r.Amount, "CA");

    private static ReportVariable Variable(string name, string formula, string? format = null) =>
        new() { Name = name, Model = "m", Formula = formula, Format = format };

    private static ReportVariable TopRegions(string top = "2", string direction = "DESC") =>
        Variable("TopRegions", $"SELECT TOP {top} [Region] ORDER BY [Amount] {direction}");

    private static VariableContext Variables(params ReportVariable[] variables)
    {
        var context = new VariableContext();
        context.Load(variables);
        return context;
    }

    private static SetSource Source(ISemanticModel model, params FilterSpec[] filters) =>
        new(name => name == model.Metadata.Name ? model : null) { Filters = _ => filters };

    private static async Task<IReadOnlyList<string?>> Members(string formula, params ReportVariable[] others) =>
        await Variables([Variable("S", formula), .. others]).MembersAsync("S", Source(Model()));

    private static async Task<ResultSet> Query(ISemanticModel model, VariableContext variables, VisualDefinition visual,
        IEnumerable<CalculatedMeasure>? measures = null)
    {
        var resolved = await variables.ResolveAsync(visual, Source(model));
        var data = await model.GetDataAsync(resolved.ToDataRequest(model.Metadata, await variables.ResolveAsync(measures ?? [], Source(model))));
        return data.Result;
    }

    private static VisualDefinition ByRegion(params MeasureRef[] values) =>
        new() { Model = "m", Type = VisualType.Table, Axis = [new("Region")], Values = values.Length > 0 ? values : [new("Amount")] };

    // --- What a formula defines

    [Fact]
    public void A_value_is_a_parameter_and_its_bounds_or_choices_give_its_control()
    {
        var slider = Assert.IsType<ParameterDefinition>(VariableFormula.Read("10 BETWEEN 3 AND 25 STEP 0.5"));
        var choices = Assert.IsType<ParameterDefinition>(VariableFormula.Read("\"Web\" IN (\"Web\"; \"Shop\")"));

        Assert.Equal((DataType.Number, "10", 3d, 25d, 0.5d), (slider.DataType, slider.Value, slider.Minimum, slider.Maximum, slider.Step));
        Assert.Equal((DataType.Text, "Web"), (choices.DataType, choices.Value));
        Assert.Equal(["Web", "Shop"], choices.Choices);
        Assert.Equal(DataType.Date, VariableFormula.Read("\"2025-01-01\"")!.DataType);
        Assert.Equal(DataType.Boolean, VariableFormula.Read("true")!.DataType);
        Assert.Equal("-2.5", ((ParameterDefinition)VariableFormula.Read("-2.5")!).Value);
    }

    [Fact]
    public void A_select_is_a_set_and_any_other_formula_a_computed_value()
    {
        var set = Assert.IsType<SetDefinition>(VariableFormula.Read("select top [@N] [Region] where [Channel] = \"Web\" order by [Amount] desc"));
        var list = Assert.IsType<SetDefinition>(VariableFormula.Read("SELECT [Region] IN (\"Nord\"; \"Est\")"));

        Assert.Equal(("Region", "N", true, true), (set.Field, set.TopVariable, set.Descending, set.HasConditions));
        Assert.False(set.IsList);
        Assert.True(list.IsList);
        Assert.Equal(["Nord", "Est"], list.Members);
        Assert.IsType<ComputedDefinition>(VariableFormula.Read("AVERAGE([Amount])"));
        Assert.IsType<ComputedDefinition>(VariableFormula.Read("10 * 2"));
    }

    [Theory]
    [InlineData("30 BETWEEN 3 AND 25", "30")]
    [InlineData("10 BETWEEN 25 AND 3", "BETWEEN")]
    [InlineData("\"a\" BETWEEN 1 AND 2", "")]
    [InlineData("\"Mail\" IN (\"Web\"; \"Shop\")", "Mail")]
    [InlineData("10 IN (10; \"Web\")", "Web")]
    [InlineData("SELECT TOP x [Région]", "TOP")]
    [InlineData("SELECT [CA]", "CA")]
    [InlineData("SELECT [Région] IN (\"Nord\"", ")")]
    [InlineData("SELECT [Région] WHERE [Canal] = [Région]", "compared with a value")]
    [InlineData("SELECT [Jour] IN (12)", "12")]
    [InlineData("SELECT [Région] ORDER BY [Canal]", "Canal")]
    [InlineData("SELECT [Région] WHERE", "")]
    [InlineData("SELECT TOP [@Missing] [Région]", "Missing")]
    public void A_formula_that_cannot_be_read_tells_why_and_where(string formula, string expected)
    {
        var problem = VariableFormula.Check(formula, Model().Metadata, out var stored);

        Assert.Null(stored);
        Assert.NotNull(problem);
        Assert.Contains(expected, problem.Message);
        Assert.InRange(problem.Start, 0, formula.Length);
    }

    [Fact]
    public void A_typed_formula_is_stored_with_field_names()
    {
        var problem = VariableFormula.Check("SELECT TOP [@N] [Région] WHERE [Canal] = \"Web\" AND [CA] > 100 ORDER BY [CA] DESC",
            Model().Metadata, out var stored, variables: [Variable("N", "2")]);

        Assert.Null(problem);
        Assert.Equal("SELECT TOP [@N] [Region] WHERE [Channel] = \"Web\" AND [Amount] > 100 ORDER BY [Amount] DESC", stored);
        Assert.Equal("SELECT TOP [@N] [Région] WHERE [Canal] = \"Web\" AND [CA] > 100 ORDER BY [CA] DESC",
            MeasureFormula.ToDisplay(stored!, Model().Metadata));
    }

    [Fact]
    public void A_variable_cannot_use_itself_through_others()
    {
        ReportVariable[] variables = [Variable("A", "[@B] + 1"), Variable("B", "[@A] * 2")];

        var problem = VariableFormula.Validate("[@B] + 1", Model().Metadata, name: "A", variables: variables);

        Assert.Contains("[@A] → [@B] → [@A]", problem);
    }

    // --- Sets

    [Fact]
    public async Task A_set_keeps_the_first_or_last_values_by_an_expression()
    {
        Assert.Equal(["Nord", "Sud"], await Variables(TopRegions()).MembersAsync("TopRegions", Source(Model())));
        Assert.Equal(["Est", "Ouest"], await Variables(TopRegions(direction: "ASC")).MembersAsync("TopRegions", Source(Model())));
        Assert.Equal(["Nord", "Sud", "Ouest"], await Variables(TopRegions("[@N]"), Variable("N", "3 BETWEEN 1 AND 5")).MembersAsync("TopRegions", Source(Model())));
        Assert.Equal(["Est", "Nord", "Ouest", "Sud"], await Members("SELECT [Region]"));
    }

    [Fact]
    public async Task Conditions_on_dimensions_keep_rows_and_conditions_on_measures_keep_values()
    {
        Assert.Equal(["Sud", "Ouest", "Nord"], await Members("SELECT [Region] WHERE [Channel] = \"Web\" ORDER BY [Amount] DESC"));
        Assert.Equal(["Nord", "Sud"], await Members("SELECT [Region] WHERE [Amount] > 150"));
        // Rows first, then the values: Nord only sells 100 on the web
        Assert.Equal(["Ouest", "Sud"], await Members("SELECT [Region] WHERE [Channel] = \"Web\" AND [Amount] >= 120"));
        Assert.Equal(["Est", "Ouest"], await Members("SELECT [Region] WHERE NOT ([Region] = \"Nord\" OR [Region] = \"Sud\")"));
        Assert.Equal(["Ouest", "Sud"], await Members("SELECT [Region] WHERE SUM([Amount]; [Channel] = \"Web\") > 100 AND COUNT() >= 1"));
    }

    [Fact]
    public async Task Conditions_compare_dates_and_variables()
    {
        Assert.Equal(["Est", "Nord", "Ouest"], await Members("SELECT [Region] WHERE [Day] >= \"2024-03-01\""));
        Assert.Equal(["Nord", "Sud"], await Members("SELECT [Region] WHERE [Day] < \"2024-03-01\""));
        Assert.Equal(["Nord"], await Members("SELECT [Region] WHERE [Day] = [@Start]", Variable("Start", "\"2024-03-05\"")));
        Assert.Equal(["Ouest", "Sud"], await Members("SELECT [Region] WHERE [Channel] = [@Channel] AND [Region] <> \"Nord\"",
            Variable("Channel", "\"Web\" IN (\"Web\"; \"Shop\")")));
    }

    [Fact]
    public async Task A_listed_set_keeps_its_values_without_a_query()
    {
        var model = new CountingModel(Model());
        var variables = Variables(Variable("S", "SELECT [Region] IN (\"Sud\"; \"Est\")"));

        Assert.Equal(["Sud", "Est"], await variables.MembersAsync("S", Source(model)));
        Assert.Equal(0, model.Requests);
        // With a condition, the listed values are only those kept
        Assert.Equal(["Sud"], await Members("SELECT [Region] IN (\"Sud\"; \"Est\") WHERE [Amount] > 100"));
    }

    [Fact]
    public async Task A_set_goes_through_the_report_filters_of_its_model()
    {
        var web = new FilterSpec("Channel", FilterOperator.In, ["Web"]);

        var members = await Variables(TopRegions()).MembersAsync("TopRegions", Source(Model(), web));

        Assert.Equal(["Sud", "Ouest"], members);
    }

    [Fact]
    public async Task The_values_of_a_set_are_computed_once_until_a_variable_changes()
    {
        var model = new CountingModel(Model());
        var variables = Variables(TopRegions("[@N]"), Variable("N", "2"));

        await variables.MembersAsync("TopRegions", Source(model));
        await variables.MembersAsync("TopRegions", Source(model));
        Assert.Equal(1, model.Requests);

        variables.SetValue("N", "3");
        Assert.Equal(3, (await variables.MembersAsync("TopRegions", Source(model))).Count);
        Assert.Equal(2, model.Requests);

        variables.Refresh();
        await variables.MembersAsync("TopRegions", Source(model));
        Assert.Equal(3, model.Requests);
    }

    // --- Values

    [Fact]
    public async Task A_parameter_changed_on_the_dashboard_keeps_its_value_until_set_back()
    {
        var variables = Variables(Variable("N", "10 BETWEEN 3 AND 25"));

        variables.SetValue("N", "12");
        Assert.Equal("12", await variables.ValueAsync("N", Source(Model())));
        Assert.Equal("12", variables.Find("N")!.Value);

        // The formula's own value is not stored
        variables.SetValue("N", "10");
        Assert.Null(variables.Find("N")!.Value);
    }

    [Fact]
    public async Task A_computed_value_is_queried_once_over_the_report_filters()
    {
        var model = new CountingModel(Model());
        var variables = Variables(Variable("Average", "AVERAGE([Amount])"));

        Assert.Equal("154", await variables.ValueAsync("Average", Source(model)));
        Assert.Equal("154", await variables.ValueAsync("Average", Source(model)));
        Assert.Equal(1, model.Requests);
        Assert.Equal("140", await variables.ValueAsync("Average", Source(Model(), new FilterSpec("Channel", FilterOperator.In, ["Web"]))));
    }

    [Fact]
    public async Task A_computed_value_aggregates_a_value_read_for_each_value_of_a_dimension()
    {
        // By region: Nord 400, Sud 200, Ouest 120, Est 50
        async Task<string?> Value(string formula, params FilterSpec[] filters) =>
            await Variables(Variable("V", formula)).ValueAsync("V", Source(Model(), filters));

        Assert.Equal("400", await Value("MAX([Amount] BY [Region])"));
        Assert.Equal("300", await Value("MAX([Amount])")); // a sale, not a region
        Assert.Equal("50", await Value("MIN([Amount] BY [Region])"));
        Assert.Equal("192.5", await Value("AVERAGE([Amount] BY [Region])"));
        Assert.Equal(400d / 770, double.Parse((await Value("MAX([Amount] BY [Region]) / SUM([Amount])"))!, CultureInfo.InvariantCulture), 6);
        Assert.Equal("200", await Value("MAX([Amount] BY [Region])", new FilterSpec("Channel", FilterOperator.In, ["Web"])));
        Assert.Equal("200", await Value("MAX(SUM([Amount]; [Channel] = \"Web\") BY [Region])"));
    }

    [Theory]
    [InlineData("MAX([CA] BY [Région])", null)]
    [InlineData("COUNT([CA] BY [Région])", "BY goes in")]
    [InlineData("MAX([CA] BY [CA])", "not a dimension")]
    [InlineData("MAX([CA] BY)", "Write MAX([value] BY [dimension])")]
    [InlineData("MAX([CA] BY [Région]; [Canal] = \"Web\")", "Write MAX([value] BY [dimension])")]
    [InlineData("SUM(MAX([CA] BY [Région]))", "cannot be used inside")]
    [InlineData("SELECT [Région] WHERE [CA] > MAX([CA] BY [Canal])", "only written in a variable")]
    public void By_reads_a_value_for_each_value_of_a_dimension_in_a_computed_value_only(string formula, string? expected)
    {
        var problem = VariableFormula.Check(formula, Model().Metadata, out var stored);

        if (expected is null)
        {
            Assert.Null(problem);
            Assert.Equal("MAX([Amount] BY [Region])", stored);
        }
        else Assert.Contains(expected, problem!.Message);
        Assert.Contains("only written in a variable", MeasureFormula.Validate("MAX([Amount] BY [Region])", Model().Metadata));
    }

    [Fact]
    public async Task Sets_and_measures_use_computed_values()
    {
        var average = Variable("Average", "AVERAGE([Amount])");
        var measure = new CalculatedMeasure { Name = "calc_gap", Model = "m", Formula = "[Amount] - [@Average]" };

        Assert.Equal(["Nord", "Sud"], await Members("SELECT [Region] WHERE [Amount] > [@Average] * 1.2", average));
        var result = await Query(Model(), Variables(average), ByRegion(new MeasureRef("calc_gap")), [measure]);
        Assert.Equal(246d, (double)result.Rows.Single(r => (string)r[0]! == "Nord")[1]!, 6);
    }

    [Fact]
    public async Task A_formula_reads_the_value_of_a_parameter()
    {
        var rate = new CalculatedMeasure { Name = "calc_ttc", Model = "m", Formula = "[Amount] * [@Rate]" };
        var channel = new CalculatedMeasure { Name = "calc_channel", Model = "m", Formula = "SUM([Amount]; [Channel] = [@Channel])" };
        var variables = Variables(Variable("Rate", "1.2"), Variable("Channel", "\"Web\""));

        var result = await Query(Model(), variables, ByRegion(new MeasureRef("calc_ttc"), new MeasureRef("calc_channel")), [rate, channel]);

        var nord = result.Rows.Single(r => (string)r[0]! == "Nord");
        Assert.Equal(480d, (double)nord[1]!, 6);
        Assert.Equal(100d, Convert.ToDouble(nord[2]));
    }

    [Fact]
    public void A_formula_may_only_use_existing_values()
    {
        var metadata = Model().Metadata;
        ReportVariable[] variables = [Variable("Rate", "1"), TopRegions()];

        Assert.Null(MeasureFormula.Validate("[Amount] * [@Rate]", metadata, variables: variables));
        Assert.Contains("Missing", MeasureFormula.Validate("[Amount] * [@Missing]", metadata, variables: variables));
        Assert.NotNull(MeasureFormula.Validate("[Amount] * [@TopRegions]", metadata, variables: variables));
        Assert.Equal(["Rate"], MeasureFormula.Variables("[Amount] * [@Rate]"));
    }

    // --- Visuals

    [Fact]
    public async Task A_filter_keeps_the_values_of_a_parameter_or_of_a_set()
    {
        var visual = ByRegion() with { Filters = [new FilterSpec("Region", FilterOperator.In, ["[@Region]", "[@TopRegions]"])] };

        var result = await Query(Model(), Variables(Variable("Region", "\"Est\""), TopRegions()), visual);

        Assert.Equal(["Est", "Nord", "Sud"], result.Rows.Select(r => r[0]));
    }

    [Fact]
    public async Task A_filter_leaves_out_the_values_of_a_set()
    {
        var visual = ByRegion() with { Filters = [new FilterSpec("Region", FilterOperator.NotIn, ["[@TopRegions]"])] };

        var result = await Query(Model(), Variables(TopRegions()), visual);

        Assert.Equal(["Est", "Ouest"], result.Rows.Select(r => r[0]));
    }

    [Fact]
    public async Task A_parameter_gives_the_number_of_groups_a_visual_keeps()
    {
        var visual = ByRegion() with { Sort = new SortSpec("Amount", Descending: true), Top = 10, TopVariable = "N" };

        var result = await Query(Model(), Variables(Variable("N", "3 BETWEEN 1 AND 10")), visual);

        Assert.Equal(["Nord", "Sud", "Ouest"], result.Rows.Select(r => r[0]));
    }

    [Fact]
    public async Task A_field_grouped_by_a_set_shows_the_set_first_then_the_others()
    {
        var visual = new VisualDefinition { Model = "m", Type = VisualType.Table, Axis = [new("Region") { Set = "TopRegions" }], Values = [new("Amount")] };
        var variables = Variables(TopRegions());

        var resolved = await variables.ResolveAsync(visual, Source(Model()));
        var result = await Query(Model(), variables, visual);

        Assert.Equal(["Nord", "Sud"], resolved.Axis[0].SetMembers);
        Assert.Equal("Region@TopRegions", result.Columns[0].Key);
        Assert.Equal([true, false], result.Rows.Select(r => r[0]));
        Assert.Equal([600m, 170m], result.Rows.Select(r => r[1]));
        Assert.Equal(["TopRegions", "Others"], result.Rows.Select(r => resolved.Axis[0].Display(r[0])));
    }

    [Fact]
    public async Task A_field_grouped_by_a_set_travels_to_a_remote_source_with_its_values()
    {
        var visual = new VisualDefinition { Model = "m", Type = VisualType.Table, Axis = [new("Region") { Set = "TopRegions" }], Values = [new("Amount")] };
        var resolved = await Variables(TopRegions()).ResolveAsync(visual, Source(Model()));

        // What a client sends, and what a server answers
        var request = DataSerializer.DeserializeRequest(DataSerializer.Serialize(resolved.ToDataRequest(Model().Metadata)));
        var data = DataSerializer.DeserializeData(DataSerializer.Serialize(await Model().GetDataAsync(request)));

        Assert.Equal(["Nord", "Sud"], request.Visual.Axis[0].SetMembers);
        Assert.Equal([true, false], data.Result.Rows.Select(r => r[0]));
    }

    [Fact]
    public async Task A_visual_keeps_its_own_labels_for_a_set()
    {
        var visual = ByRegion() with { Axis = [new FieldRef("Region") { Set = "TopRegions" }.WithLabel(false, "Le reste")] };

        var resolved = await Variables(TopRegions()).ResolveAsync(visual, Source(Model()));

        Assert.Equal("Le reste", resolved.Axis[0].Display(false));
        Assert.Equal("TopRegions", resolved.Axis[0].Display(true));
    }

    [Fact]
    public async Task Clicking_the_set_or_the_others_filters_on_the_set()
    {
        var visual = ByRegion() with { Axis = [new FieldRef("Region") { Set = "TopRegions" }] };
        var variables = Variables(TopRegions());
        var resolved = await variables.ResolveAsync(visual, Source(Model()));

        var inSet = new SelectedElement(resolved.Axis, [true]).Filters.Single();
        var others = new SelectedElement(resolved.Axis, [false]).Filters.Single();
        var both = new CrossFilter("v", "m", [new SelectedElement(resolved.Axis, [true]), new SelectedElement(resolved.Axis, [false])]).Filters.Single();

        Assert.Equal(FilterOperator.In, inSet.Operator);
        Assert.Equal(["[@TopRegions]"], inSet.Values);
        Assert.Equal(FilterOperator.NotIn, others.Operator);
        Assert.Equal(["[@TopRegions]"], others.Values);
        var result = await Query(Model(), variables, ByRegion() with { Filters = [others] });
        Assert.Equal(["Est", "Ouest"], result.Rows.Select(r => r[0]));
        Assert.Equal(FilterOperator.AnyOf, both.Operator);
    }

    [Fact]
    public async Task A_matrix_puts_a_set_on_its_rows()
    {
        var visual = new VisualDefinition
        {
            Model = "m",
            Type = VisualType.Matrix,
            Axis = [new("Region") { Set = "TopRegions" }],
            Legend = new("Channel"),
            Values = [new("Amount")],
        };

        var result = await Query(Model(), Variables(TopRegions()), visual);

        Assert.Equal(4, result.Rows.Count);
    }

    [Fact]
    public async Task Titles_and_texts_show_the_values_of_variables()
    {
        var visual = ByRegion() with { Title = "Top {N} – {TopRegions} – {Missing} – {{N}} – {Average}" };
        var text = new VisualDefinition { Type = VisualType.Text, Text = new TextContent { Text = "Taux : {Taux de TVA}" } };
        var variables = Variables(Variable("N", "2"), Variable("Taux de TVA", "0.125", "P1"), TopRegions(), Variable("Average", "AVERAGE([Amount])", "N0"));

        var resolved = await variables.ResolveAsync(visual, Source(Model()));
        var resolvedText = await variables.ResolveAsync(text, Source(Model()));

        Assert.Equal("Top 2 – Nord, Sud – {Missing} – {N} – 154", resolved.Title);
        Assert.Equal("Taux : 12.5 %", resolvedText.Text!.Text);
    }

    [Fact]
    public async Task A_visual_referencing_a_missing_variable_cannot_be_queried()
    {
        var visual = ByRegion() with { Filters = [new FilterSpec("Region", FilterOperator.In, ["[@Missing]"])] };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Variables().ResolveAsync(visual, Source(Model())));

        Assert.Contains("Missing", error.Message);
    }

    // --- Reports

    [Fact]
    public void Reports_without_variables_keep_their_json_and_variables_round_trip()
    {
        var plain = ReportSerializer.Serialize(new ReportDefinition());
        Assert.DoesNotContain("variables", plain);

        var report = new ReportDefinition { Variables = [Variable("N", "10 BETWEEN 3 AND 25") with { Value = "12" }, TopRegions("[@N]")] };
        var read = ReportSerializer.Deserialize(ReportSerializer.Serialize(report));

        Assert.Equal(report.Variables, read.Variables);
    }

    [Fact]
    public void The_report_validator_reports_missing_and_invalid_variables()
    {
        var report = new ReportDefinition
        {
            Visuals = [ByRegion() with { Title = "Top {N}", TopVariable = "N", Filters = [new FilterSpec("Region", FilterOperator.In, ["[@Missing]"])] }],
            Variables = [TopRegions("[@Count]"), Variable("bad [name]", "1"), Variable("Broken", "SELECT [Nowhere]"), new ReportVariable { Name = "Free", Formula = "5" }],
        };

        var problems = ReportValidator.Validate(report, [Model().Metadata]);

        Assert.Contains(problems, p => p.Contains("Missing"));
        Assert.Contains(problems, p => p.Contains("\"N\""));
        Assert.Contains(problems, p => p.Contains("Count"));
        Assert.Contains(problems, p => p.Contains("bad [name]"));
        Assert.Contains(problems, p => p.Contains("Nowhere"));
        // A parameter reads no field: it needs no model
        Assert.DoesNotContain(problems, p => p.Contains("Free"));
    }

    [Fact]
    public void Copied_visuals_bring_the_variables_they_use()
    {
        var variables = Variables(Variable("N", "3"), TopRegions("[@N]"), Variable("Unused", "1"));
        var visual = ByRegion() with { Axis = [new FieldRef("Region") { Set = "TopRegions" }] };
        var json = VisualTransfer.Copy([visual], new MeasureContext(), variables: variables);

        var target = Variables(Variable("N", "7"));
        VisualTransfer.Paste(json, new MeasureContext(), _ => true, variables: target);

        Assert.Equal(["N", "TopRegions"], target.Variables.Select(v => v.Name));
        // The target keeps its own variable of the same name
        Assert.Equal("7", target.Find("N")!.Formula);
    }

    private sealed class CountingModel(ISemanticModel inner) : ISemanticModel
    {
        public int Requests { get; private set; }

        public ModelMetadata Metadata => inner.Metadata;

        public Task<VisualData> GetDataAsync(VisualDataRequest request, CancellationToken cancellationToken = default)
        {
            Requests++;
            return inner.GetDataAsync(request, cancellationToken);
        }

        public Task<DistinctValues> GetDistinctValuesAsync(DistinctValuesRequest request, CancellationToken cancellationToken = default) =>
            inner.GetDistinctValuesAsync(request, cancellationToken);
    }
}
