using System.Threading;
using System.Threading.Tasks;

namespace IronDev.Core.Interfaces;

/// <summary>
/// Converts Markdown content to HTML for display in product document viewers.
/// </summary>
public interface IMarkdownRenderService
{
    /// <summary>Converts Markdown to an HTML fragment (body content only).</summary>
    string ToHtml(string markdown);

    /// <summary>
    /// Converts Markdown to a complete, self-contained styled HTML document
    /// suitable for loading directly into WebView2.
    /// </summary>
    string ToStyledHtmlDocument(string markdown);
}
