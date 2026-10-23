using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;

namespace WWP.LandscapeDataManager.App.Parameters;

/// <summary>
/// Save shared parameter file…: the shared parameter file built into this LIM version, byte for byte,
/// so the same parameters (same GUIDs) can be loaded by hand, e.g. into a tag family to label one.
/// It's the file Load parameters and Apply bind from, embedded here as in the Revit connector.
/// </summary>
public sealed partial class MainPage
{
    private const string SharedParameterResourceName = "LIM.LandscapeData.SharedParameters.txt";

    private async void SaveSharedParameterFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            byte[] bytes;
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(SharedParameterResourceName)
                                  ?? throw new InvalidOperationException("This build has no shared parameter file in it."))
            using (var memory = new MemoryStream())
            {
                await resource.CopyToAsync(memory);
                bytes = memory.ToArray();
            }

            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = version is null ? "LIM shared parameters" : $"LIM shared parameters {version}"
            };
            picker.FileTypeChoices.Add("Revit shared parameter file", [".txt"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
            var target = await picker.PickSaveFileAsync();
            if (target is null)
            {
                return;
            }

            await File.WriteAllBytesAsync(target.Path, bytes);

            // Revit writes these files as UTF-16; the PARAM rows are the parameters.
            var parameters = Encoding.Unicode.GetString(bytes).Split('\n').Count(line => line.StartsWith("PARAM\t", StringComparison.Ordinal));
            ShowStatus($"Saved {target.Name}: {parameters:N0} parameters with the GUIDs this tool binds. To use one in a family (a tag's label, say), " +
                       "Manage › Shared Parameters › Browse to this file, then add the parameter. Revit remembers the last file you browse to, " +
                       "so point it back at your usual shared parameter file afterwards.");
        }
        catch (Exception exception)
        {
            ShowStatus($"Couldn't save the shared parameter file: {exception.Message}");
        }
    }
}
