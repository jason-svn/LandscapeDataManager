using Autodesk.Revit.DB;

namespace WWP.LandscapeDataManager.Revit.Services;

internal static class ElementParameters
{
    /// <summary>
    /// The named parameter on <paramref name="element"/>, else on its type. <c>LookupParameter</c>
    /// on an instance never sees Type-bound parameters, and several LIM parameters (e.g.
    /// <c>!_S_PLT_LDS_Type_Text</c>, <c>!_S_PLT_LDS_SurfaceClass_Text</c>) are bound to the floor
    /// Type — reading or writing them on the instance alone silently finds nothing.
    /// </summary>
    public static Parameter? LookupOnInstanceOrType(Element element, string name) =>
        element.LookupParameter(name) ??
        (element.Document.GetElement(element.GetTypeId()) as ElementType)?.LookupParameter(name);
}
