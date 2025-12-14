using System.Text;

namespace DependencyCalculator.Services;

/// <summary>
/// Reads NPM configuration from .npmrc file
/// </summary>
public class NpmConfigReader
{
    public string? Registry { get; private set; }
    public string? Username { get; private set; }
    public string? Password { get; private set; }
    public bool StrictSsl { get; private set; } = true;

    public static NpmConfigReader ReadFromFile(string npmrcPath)
    {
        var config = new NpmConfigReader();
        
        if (!File.Exists(npmrcPath))
        {
            throw new FileNotFoundException($".npmrc file not found at: {npmrcPath}");
        }

        var lines = File.ReadAllLines(npmrcPath);
        string? registryHost = null;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // Skip comments and empty lines
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
            {
                continue;
            }

            // Parse key=value pairs
            var parts = trimmedLine.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var key = parts[0].Trim();
            var value = parts[1].Trim();

            // Handle environment variable substitution
            value = ExpandEnvironmentVariables(value);

            if (key == "registry")
            {
                config.Registry = value;
                // Extract host from registry URL
                var uri = new Uri(value);
                registryHost = uri.Host + uri.AbsolutePath.TrimEnd('/');
            }
            else if (key == "strict-ssl")
            {
                config.StrictSsl = bool.Parse(value);
            }
            else if (registryHost != null && key.StartsWith($"//{registryHost}/:username"))
            {
                config.Username = value;
            }
            else if (registryHost != null && key.StartsWith($"//{registryHost}/:_password"))
            {
                // Decode base64 password
                try
                {
                    var passwordBytes = Convert.FromBase64String(value);
                    config.Password = Encoding.UTF8.GetString(passwordBytes);
                }
                catch
                {
                    // If not base64, use as-is
                    config.Password = value;
                }
            }
        }

        return config;
    }

    private static string ExpandEnvironmentVariables(string value)
    {
        // Handle ${ENV_VAR} syntax
        var result = value;
        var startIndex = 0;
        
        while (startIndex < result.Length)
        {
            var openIndex = result.IndexOf("${", startIndex);
            if (openIndex == -1)
                break;
                
            var closeIndex = result.IndexOf("}", openIndex);
            if (closeIndex == -1)
                break;
                
            var envVarName = result.Substring(openIndex + 2, closeIndex - openIndex - 2);
            var envVarValue = Environment.GetEnvironmentVariable(envVarName) ?? "";
            
            result = result.Substring(0, openIndex) + envVarValue + result.Substring(closeIndex + 1);
            startIndex = openIndex + envVarValue.Length;
        }
        
        // Also handle %ENV_VAR% syntax (Windows)
        result = Environment.ExpandEnvironmentVariables(result);
        
        return result;
    }
}
