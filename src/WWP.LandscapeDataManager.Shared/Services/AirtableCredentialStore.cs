using Windows.Security.Credentials;

namespace WWP.LandscapeDataManager.Shared.Services;

/// <summary>Stores the Airtable personal access token in Windows Credential Manager for the current user.</summary>
public sealed class AirtableCredentialStore
{
    private const string ResourceName = "EGIS.WWP.LandscapeDataManager.Airtable";
    private const string UserName = "AirtableApi";

    public string Load()
    {
        var environmentValue = Environment.GetEnvironmentVariable("AIRTABLE_API_TOKEN");
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

    public void Save(string apiToken)
    {
        Delete();
        if (!string.IsNullOrWhiteSpace(apiToken))
        {
            new PasswordVault().Add(new PasswordCredential(ResourceName, UserName, apiToken.Trim()));
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
            // No saved Airtable credential exists.
        }
    }
}
