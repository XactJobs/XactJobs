using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace XactJobs.UI.DependencyInjection;

internal class XactJobsPageRouteModelConvention : IPageRouteModelConvention
{
    private const string DefaultPrefix = "XactJobs/";

    private readonly string _basePath;

    public XactJobsPageRouteModelConvention(string basePath)
    {
        _basePath = basePath.Trim('/');
    }

    public void Apply(PageRouteModel model)
    {
        if (model.AreaName != "XactJobs") return;

        foreach (var selector in model.Selectors)
        {
            if (selector.AttributeRouteModel?.Template != null 
                && selector.AttributeRouteModel.Template.StartsWith(DefaultPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Prefix the route with the configured base path
                selector.AttributeRouteModel.Template =
                    AttributeRouteModel.CombineTemplates(_basePath, selector.AttributeRouteModel.Template[DefaultPrefix.Length..]);
            }
        }
    }
}
