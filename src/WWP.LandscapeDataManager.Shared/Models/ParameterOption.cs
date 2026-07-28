using WWP.LandscapeDataManager.Contracts;

namespace WWP.LandscapeDataManager.App.Models;

public sealed class ParameterOption
{
    public ParameterOption(RevitParameterDescriptor descriptor)
    {
        Descriptor = descriptor;
        DisplayName = $"{descriptor.Name}  ·  {descriptor.Scope}  ·  {descriptor.StorageType}";
    }

    public RevitParameterDescriptor Descriptor { get; }
    public string DisplayName { get; }
}
