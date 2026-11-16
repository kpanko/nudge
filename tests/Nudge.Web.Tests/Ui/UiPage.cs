using System.Net;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace Nudge.Web.Tests.Ui;

/// <summary>
/// A page of the web UI as the browser got it, and its forms sent as
/// the browser would send them: every field with its value as shown
/// (a checkbox only when ticked, a select's chosen option), the
/// antiforgery token and the form's key included, changed only where
/// a test says.
/// </summary>
internal sealed class UiPage
{
    private readonly WebUiHost _host;

    private UiPage(
        WebUiHost host,
        string path,
        string html)
    {
        _host = host;
        Path = path;
        Html = html;
        Document = new HtmlParser().ParseDocument(html);
    }

    public string Path { get; }

    public string Html { get; }

    public IHtmlDocument Document { get; }

    public static async Task<UiPage> OpenAsync(
        WebUiHost host,
        string path)
    {
        var reply = await host.GetAsync(path);
        var html = await reply.Content.ReadAsStringAsync();
        Assert.True(
            reply.StatusCode == HttpStatusCode.OK,
            $"GET {path}: {(int)reply.StatusCode} {html}");
        return new UiPage(host, path, html);
    }

    /// <summary>The page a form's answer showed, when it was not a
    /// redirect (a refusal shown in the form).</summary>
    public static async Task<UiPage> FromAsync(
        WebUiHost host,
        string path,
        HttpResponseMessage reply)
    {
        var html = await reply.Content.ReadAsStringAsync();
        Assert.True(
            reply.StatusCode == HttpStatusCode.OK,
            $"{path}: {(int)reply.StatusCode} {html}");
        return new UiPage(host, path, html);
    }

    /// <summary>The texts of the elements a selector finds, with
    /// runs of white space made one space.</summary>
    public List<string> Texts(string selector)
    {
        return Document
            .QuerySelectorAll(selector)
            .Select(
                e => string.Join(
                    ' ',
                    e.TextContent.Split(
                        (char[]?)null,
                        StringSplitOptions.RemoveEmptyEntries)))
            .ToList();
    }

    public bool HasForm(string name)
    {
        return FindForm(name) is not null;
    }

    /// <summary>The fields a form would send, by name.</summary>
    public Dictionary<string, string> Fields(string name)
    {
        var form = FindForm(name)
            ?? throw new InvalidOperationException(
                $"{Path} has no form {name}.");
        var fields = new Dictionary<string, string>();
        foreach (var element in form.Elements)
        {
            switch (element)
            {
                case IHtmlInputElement input
                    when input.Name is { Length: > 0 } inputName
                        && input.Type is not ("checkbox" or "radio" or "submit"):
                    fields[inputName] = input.Value;
                    break;
                case IHtmlInputElement box
                    when box.Name is { Length: > 0 } boxName
                        && box.Type is "checkbox" or "radio"
                        && box.IsChecked:
                    fields[boxName] = box.Value;
                    break;
                case IHtmlSelectElement select
                    when select.Name is { Length: > 0 } selectName:
                    var chosen = select.Options
                        .FirstOrDefault(o => o.IsSelected)
                        ?? select.Options.FirstOrDefault();
                    fields[selectName] = chosen?.Value ?? "";
                    break;
                case IHtmlTextAreaElement area
                    when area.Name is { Length: > 0 } areaName:
                    fields[areaName] = area.Value;
                    break;
            }
        }

        return fields;
    }

    /// <summary>Sends a form, with these fields changed (a null
    /// value leaves a field out).</summary>
    public Task<HttpResponseMessage> SubmitAsync(
        string name,
        params (string Name, string? Value)[] changes)
    {
        return SendAsync(Fields(name), changes);
    }

    /// <summary>Sends fields read from a form earlier, as a page
    /// left open and sent again would.</summary>
    public Task<HttpResponseMessage> SendAsync(
        Dictionary<string, string> fields,
        params (string Name, string? Value)[] changes)
    {
        var sent = new Dictionary<string, string>(fields);
        foreach (var (field, value) in changes)
        {
            if (value is null)
            {
                sent.Remove(field);
            }
            else
            {
                sent[field] = value;
            }
        }

        return _host.SendAsync(
            HttpMethod.Post,
            Path,
            new FormUrlEncodedContent(sent));
    }

    private IHtmlFormElement? FindForm(string name)
    {
        return Document.Forms.SingleOrDefault(
            form => form.Elements
                .OfType<IHtmlInputElement>()
                .Any(i => i.Name == "_handler" && i.Value == name));
    }
}
