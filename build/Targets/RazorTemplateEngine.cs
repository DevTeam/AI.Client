namespace Build.Targets;

using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.Hosting;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

internal sealed class RazorTemplateEngine : ITemplateEngine
{
    private static readonly IReadOnlyList<RazorCompiledItem> CompiledItems =
        new RazorCompiledItemLoader().LoadItems(typeof(RazorTemplateEngine).Assembly);

    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    public async Task RenderAsync<TModel>(
        string templateName,
        TModel model,
        Stream stream,
        CancellationToken cancellationToken)
    {
        var item = CompiledItems.FirstOrDefault(item => item.Identifier == templateName)
                   ?? throw new InvalidOperationException($"Cannot find Razor template '{templateName}'.");
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        var page = (RazorPage<TModel>)Activator.CreateInstance(item.Type)!;
        page.ViewData = new ViewDataDictionary<TModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };
        page.ViewContext = new ViewContext { Writer = writer };
        page.HtmlEncoder = Encoder;
        await page.ExecuteAsync();
        await writer.FlushAsync(cancellationToken);
    }
}
