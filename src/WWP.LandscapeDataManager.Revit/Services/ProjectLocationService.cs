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
    private const string LatitudeParameter = "!_S_PLT_iTreeLocation_Latitude_Number";
    private const string LongitudeParameter = "!_S_PLT_iTreeLocation_Longitude_Number";

    public static ProjectSiteLocationResult GetSiteLocation(UIApplication application)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before reading its location.");

        var siteLocation = document.SiteLocation;
        var siteLatitude = siteLocation.Latitude * (180.0 / Math.PI);
        var siteLongitude = siteLocation.Longitude * (180.0 / Math.PI);

        // Location Finder publishes to the i-Tree parameters, not Revit's Site Location, and those
        // are what every calculation uses — so they win when set. Revit's place name is only kept
        // when it still describes (roughly) the same spot.
        var projectInfo = document.ProjectInformation;
        if (GetDouble(projectInfo, LatitudeParameter) is { } latitude &&
            GetDouble(projectInfo, LongitudeParameter) is { } longitude &&
            (latitude != 0 || longitude != 0))
        {
            var samePlace = Math.Abs(latitude - siteLatitude) < 0.01 && Math.Abs(longitude - siteLongitude) < 0.01;
            return new ProjectSiteLocationResult(document.Title, latitude, longitude, samePlace ? siteLocation.PlaceName : null);
        }

        return new ProjectSiteLocationResult(document.Title, siteLatitude, siteLongitude, siteLocation.PlaceName);
    }

    private static double? GetDouble(Element element, string name) =>
        element.LookupParameter(name) is { HasValue: true, StorageType: StorageType.Double } parameter ? parameter.AsDouble() : null;

    public static PublishProjectLocationResult Publish(UIApplication application, PublishProjectLocationRequest request)
    {
        var document = application.ActiveUIDocument?.Document
                       ?? throw new InvalidOperationException("Open a Revit project before publishing a location.");

        var projectInfo = document.ProjectInformation;
        var missing = new[] { LatitudeParameter, LongitudeParameter }
            .Where(name => projectInfo.LookupParameter(name) is not { IsReadOnly: false })
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Project Information has no writable {string.Join(" / ", missing)} — run Import Shared Parameter first, then publish again.");
        }

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
