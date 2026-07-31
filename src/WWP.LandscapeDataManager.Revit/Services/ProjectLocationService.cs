using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.Revit.Services;

/// <summary>
/// Reads/writes the project's location for the i-Tree workflow: the built-in Revit Site Location
/// (for "does this file already have a location set") and the two Project Information
/// shared-parameter fields i-Tree calculations actually read from (see
/// <see cref="PlantingInstanceValidationScanner"/>).
/// </summary>
internal static class ProjectLocationService
{
    private const string LatitudeParameter = "!_S_PLANTING_iTreeLocation_Latitude_Number";
    private const string LongitudeParameter = "!_S_PLANTING_iTreeLocation_Longitude_Number";

    public static ProjectSiteLocationResult GetSiteLocation(UIApplication application)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before reading its location.");

        var siteLocation = document.SiteLocation;
        var latitude = siteLocation.Latitude * (180.0 / Math.PI);
        var longitude = siteLocation.Longitude * (180.0 / Math.PI);
        return new ProjectSiteLocationResult(document.Title, latitude, longitude, siteLocation.PlaceName);
    }

    public static PublishProjectLocationResult Publish(UIApplication application, PublishProjectLocationRequest request)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before publishing a location.");

        var projectInfo = document.ProjectInformation;
        using var transaction = new Transaction(document, "LIM Publish Project Location");
        transaction.Start();
        try
        {
            SetIfWritable(projectInfo.LookupParameter(LatitudeParameter), request.Latitude);
            SetIfWritable(projectInfo.LookupParameter(LongitudeParameter), request.Longitude);
            transaction.Commit();
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }

        return new PublishProjectLocationResult(document.Title, request.Latitude, request.Longitude);
    }

    private static void SetIfWritable(Parameter? parameter, double value)
    {
        if (parameter is { IsReadOnly: false })
        {
            parameter.Set(value);
        }
    }
}
