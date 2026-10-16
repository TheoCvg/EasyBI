using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Wpf;

/// <summary>
/// Affiche un <see cref="VisualDefinition"/> : exécute la requête puis délègue
/// le dessin au renderer résolu par le contexte.
/// </summary>
public class VisualView : ContentControl
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(EasyBIContext), typeof(VisualView),
        new PropertyMetadata(null, (d, e) => ((VisualView)d).OnContextChanged((EasyBIContext?)e.OldValue)));

    public static readonly DependencyProperty DefinitionProperty = DependencyProperty.Register(
        nameof(Definition), typeof(VisualDefinition), typeof(VisualView),
        new PropertyMetadata(null, (d, _) => ((VisualView)d).Refresh()));

    public static readonly DependencyProperty ShowTitleProperty = DependencyProperty.Register(
        nameof(ShowTitle), typeof(bool), typeof(VisualView), new PropertyMetadata(true, (d, _) => ((VisualView)d).Refresh()));

    private CancellationTokenSource? _pending;

    public VisualView()
    {
        Loaded += (_, _) =>
        {
            if (Context is { } context) context.Changed += OnContextUpdated;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            if (Context is { } context) context.Changed -= OnContextUpdated;
        };
    }

    public EasyBIContext? Context
    {
        get => (EasyBIContext?)GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    public VisualDefinition? Definition
    {
        get => (VisualDefinition?)GetValue(DefinitionProperty);
        set => SetValue(DefinitionProperty, value);
    }

    public bool ShowTitle
    {
        get => (bool)GetValue(ShowTitleProperty);
        set => SetValue(ShowTitleProperty, value);
    }

    /// <summary>Nom du renderer utilisé pour le dernier rendu (utile pour le debug).</summary>
    public string? RenderedBy { get; private set; }

    private void OnContextChanged(EasyBIContext? old)
    {
        if (IsLoaded)
        {
            if (old is not null) old.Changed -= OnContextUpdated;
            if (Context is { } context) context.Changed += OnContextUpdated;
        }
        Refresh();
    }

    private void OnContextUpdated(object? sender, EventArgs e) => Dispatcher.InvokeAsync(Refresh);

    public async void Refresh()
    {
        _pending?.Cancel();
        if (!IsLoaded || Context is not { } context || Definition is not { } visual)
            return;

        if (VisualValidator.Validate(visual) is { } problem)
        {
            Show(visual, Message(problem));
            return;
        }

        var renderer = context.ResolveRenderer(visual.Mark);
        if (renderer is null)
        {
            Show(visual, Message($"Aucun renderer ne sait afficher « {visual.Mark} »."));
            return;
        }

        var cts = _pending = new CancellationTokenSource();
        try
        {
            var request = await VisualPipeline.PrepareAsync(context.GetModel(visual.Model), visual, context.Theme, cts.Token);
            if (cts.IsCancellationRequested) return;
            RenderedBy = renderer.Name;
            Show(visual, renderer.Render(request));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
                Show(visual, Message($"Erreur : {ex.Message}", Brushes.Firebrick));
        }
    }

    private void Show(VisualDefinition visual, UIElement body)
    {
        var root = new DockPanel { LastChildFill = true };
        if (ShowTitle && !string.IsNullOrWhiteSpace(visual.Title))
        {
            var title = new TextBlock
            {
                Text = visual.Title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 0, 4, 6),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            DockPanel.SetDock(title, Dock.Top);
            root.Children.Add(title);
        }
        root.Children.Add(body);
        Content = root;
    }

    private static TextBlock Message(string text, Brush? brush = null) => new()
    {
        Text = text,
        Foreground = brush ?? Brushes.Gray,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(16),
    };
}
