using System.Reflection;
using System.Text;
using EasyBI.Abstractions;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf;

/// <summary>What a visual was doing when it failed.</summary>
public enum VisualFailureStage
{
    /// <summary>Running its query on the model (translating it for a database, reading the data...).</summary>
    Query,

    /// <summary>Drawing the result with its renderer.</summary>
    Render,
}

/// <summary>
/// A visual that could not be shown (see <see cref="EasyBIContext.VisualFailed"/>): log <see cref="Exception"/>, change
/// <see cref="Message"/>, or hide the details from the end user with <see cref="ShowDetails"/>.
/// </summary>
public sealed class VisualFailedEventArgs(VisualDefinition visual, Exception exception, VisualFailureStage stage, string? renderer) : EventArgs
{
    public VisualDefinition Visual { get; } = visual;

    public Exception Exception { get; } = exception;

    public VisualFailureStage Stage { get; } = stage;

    /// <summary>The renderer that failed to draw the visual; null when its query failed.</summary>
    public string? Renderer { get; } = renderer;

    /// <summary>What the visual shows in place of its content: a short text for the end user.</summary>
    public string Message { get; set; } = Strings.VisualFailed;

    /// <summary>Whether the end user can open the details (<see cref="Details"/>) of the failure. True by default.</summary>
    public bool ShowDetails { get; set; } = true;

    /// <summary>
    /// The text the details show and copy, for a bug report: the visual, the stage, the version of EasyBI and the
    /// exception with its inner exceptions and stack traces.
    /// </summary>
    public string Details
    {
        get
        {
            var text = new StringBuilder();
            var name = string.IsNullOrWhiteSpace(Visual.Title) ? Visual.Type.ToString() : $"{Visual.Title} ({Visual.Type})";
            text.AppendLine($"Visual: {name}, model '{Visual.Model}'");
            text.AppendLine(Renderer is null ? $"Stage: {Stage}" : $"Stage: {Stage}, renderer '{Renderer}'");
            text.AppendLine($"EasyBI: {Version}");
            text.AppendLine();
            text.Append(Exception);
            return text.ToString();
        }
    }

    private static string Version =>
        typeof(VisualFailedEventArgs).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(VisualFailedEventArgs).Assembly.GetName().Version?.ToString()
        ?? "?";
}
