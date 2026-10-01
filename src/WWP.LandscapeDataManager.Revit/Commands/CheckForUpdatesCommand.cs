using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WWP.LandscapeDataManager.Contracts;
using WWP.LandscapeDataManager.Revit.Services;

namespace WWP.LandscapeDataManager.Revit.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class CheckForUpdatesCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        LimRelease? release;
        try
        {
            // One small JSON request with a 15 s timeout, so a short UI block is acceptable here.
            release = Task.Run(() => UpdateService.GetLatestReleaseAsync(CancellationToken.None)).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            TaskDialog.Show(
                "LIM updates",
                $"Couldn't reach GitHub to check for updates ({exception.GetBaseException().Message}).\n\n" +
                $"You have {UpdateService.InstalledVersionText}. Releases: {LimReleaseFeed.ReleasesPageUrl}");
            return Result.Succeeded;
        }

        if (release is null || !LimReleaseFeed.IsNewer(release.Version, UpdateService.InstalledVersion))
        {
            TaskDialog.Show("LIM updates", $"You have the latest version of LIM- Landscape Data ({UpdateService.InstalledVersionText}).");
            return Result.Succeeded;
        }

        UpdatePrompt.Show(release, commandData.Application.Application.VersionNumber, offerSkip: false);
        return Result.Succeeded;
    }
}

/// <summary>The "update available" dialog, shared by the ribbon button and the startup check.</summary>
internal static class UpdatePrompt
{
    private const int MaxNotesLength = 1500;

    public static void Show(LimRelease release, string revitVersion, bool offerSkip)
    {
        if (UpdateService.IsScheduled(release))
        {
            TaskDialog.Show("LIM updates", $"{release.Tag} is already downloading in its own window. It installs when you close Revit.");
            return;
        }

        var notes = release.Notes.Length > MaxNotesLength ? release.Notes[..MaxNotesLength] + "…" : release.Notes;
        var dialog = new TaskDialog("LIM update available")
        {
            MainInstruction = $"{release.Name} is available",
            MainContent = $"You have {UpdateService.InstalledVersionText}. The update downloads in a separate window while you keep " +
                          "working, then installs once you close Revit.",
            ExpandedContent = notes.Length > 0 ? notes : null,
            FooterText = release.PageUrl,
            CommonButtons = TaskDialogCommonButtons.Close,
            DefaultButton = TaskDialogResult.Close
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Download and install when Revit closes");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Remind me later");
        if (offerSkip)
        {
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, $"Skip {release.Tag}", "Don't remind me about this version again.");
        }

        switch (dialog.Show())
        {
            case TaskDialogResult.CommandLink1:
                try
                {
                    UpdateService.ScheduleInstall(release, revitVersion);
                }
                catch (Exception exception)
                {
                    TaskDialog.Show("LIM updates", $"Couldn't start the update: {exception.Message}");
                }

                break;
            case TaskDialogResult.CommandLink3:
                UpdateService.SkipVersion(release);
                break;
        }
    }
}
