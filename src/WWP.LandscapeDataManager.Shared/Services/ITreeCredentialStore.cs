using Windows.Security.Credentials;

namespace WWP.LandscapeDataManager.App.Services;

internal sealed class ITreeCredentialStore
{
    private const string ResourceName = "EGIS.WWP.LandscapeDataManager.iTree";
    private const string UserName = "iTreeApi";

    public string Load()
    {
        var environmentValue = Environment.GetEnvironmentVariable("ITREE_API_KEY");
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        try
        {
            var credential = new PasswordVault().Retrieve(ResourceName, UserName);
            credential.RetrievePassword();
            return credential.Password ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Save(string apiKey)
    {
        Delete();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            new PasswordVault().Add(new PasswordCredential(ResourceName, UserName, apiKey.Trim()));
        }
    }

    public void Delete()
    {
        var vault = new PasswordVault();
        try
        {
            foreach (var credential in vault.FindAllByResource(ResourceName))
            {
                vault.Remove(credential);
            }
        }
        catch
        {
            // No saved i-Tree credential exists.
        }
    }
}
