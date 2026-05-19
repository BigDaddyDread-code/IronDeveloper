using Markdig;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// Converts Markdown to styled HTML using Markdig.
/// Raw HTML passthrough is disabled for safety — all content is LLM/user generated.
/// </summary>
public sealed class MarkdownRenderService : IMarkdownRenderService
{
    private readonly MarkdownPipeline _pipeline;

    public MarkdownRenderService()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()  // tables, task lists, auto-links, definition lists, etc.
            .DisableHtml()            // block raw HTML injection from LLM-generated docs
            .Build();
    }

    public string ToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        return Markdown.ToHtml(markdown, _pipeline);
    }

    public string ToStyledHtmlDocument(string markdown)
    {
        var body = ToHtml(markdown);

        return $$"""
        <!doctype html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <style>
                * { box-sizing: border-box; margin: 0; padding: 0; }

                body {
                    font-family: "Segoe UI", system-ui, Arial, sans-serif;
                    background: #0D1117;
                    color: #E6EDF3;
                    padding: 28px 32px 48px 32px;
                    line-height: 1.7;
                    font-size: 14px;
                    max-width: 900px;
                }

                h1, h2, h3, h4 {
                    color: #F0F6FC;
                    font-weight: 600;
                    margin-top: 1.5em;
                    margin-bottom: 0.5em;
                    line-height: 1.3;
                }

                h1 {
                    font-size: 24px;
                    border-bottom: 1px solid #30363D;
                    padding-bottom: 10px;
                    margin-top: 0;
                }

                h2 { font-size: 19px; }
                h3 { font-size: 16px; color: #CDD9E5; }
                h4 { font-size: 14px; color: #8B949E; text-transform: uppercase; letter-spacing: 0.06em; }

                p { margin-bottom: 1em; }

                ul, ol {
                    padding-left: 1.6em;
                    margin-bottom: 1em;
                }

                li { margin-bottom: 0.35em; }
                li > ul, li > ol { margin-top: 0.35em; margin-bottom: 0; }

                code {
                    background: #161B22;
                    color: #FFA657;
                    padding: 2px 6px;
                    border-radius: 4px;
                    font-family: "Cascadia Code", "Consolas", monospace;
                    font-size: 13px;
                }

                pre {
                    background: #0B1020;
                    border: 1px solid #30363D;
                    border-radius: 8px;
                    padding: 16px 20px;
                    overflow-x: auto;
                    margin-bottom: 1.2em;
                }

                pre code {
                    background: transparent;
                    padding: 0;
                    color: #79C0FF;
                    font-size: 13px;
                }

                blockquote {
                    border-left: 3px solid #388BFD;
                    margin: 1em 0;
                    padding: 8px 16px;
                    background: #161B22;
                    border-radius: 0 6px 6px 0;
                    color: #8B949E;
                }

                table {
                    border-collapse: collapse;
                    width: 100%;
                    margin-bottom: 1.2em;
                    font-size: 13px;
                }

                th {
                    background: #161B22;
                    color: #CDD9E5;
                    font-weight: 600;
                    padding: 8px 12px;
                    border: 1px solid #30363D;
                    text-align: left;
                }

                td {
                    padding: 7px 12px;
                    border: 1px solid #21262D;
                    vertical-align: top;
                }

                tr:nth-child(even) td { background: #0D1117; }
                tr:nth-child(odd)  td { background: #0B0F14; }

                a { color: #58A6FF; text-decoration: none; }
                a:hover { text-decoration: underline; }

                hr {
                    border: none;
                    border-top: 1px solid #30363D;
                    margin: 1.8em 0;
                }

                /* Task list checkboxes */
                input[type="checkbox"] { margin-right: 6px; }

                /* Definition lists */
                dl { margin-bottom: 1em; }
                dt { font-weight: 600; color: #CDD9E5; margin-top: 0.8em; }
                dd { margin-left: 1.4em; color: #8B949E; }
            </style>
        </head>
        <body>
            {{body}}
        </body>
        </html>
        """;
    }
}
