using System.Text.RegularExpressions;
using ShuRuk.Contracts.Models;

namespace ShuRuk.Runtime;

public class ManifestValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public static ManifestValidationResult Success() => new() { IsValid = true };
    public static ManifestValidationResult Failure(IReadOnlyList<string> errors) => new() { IsValid = false, Errors = errors };
}

public partial class ModuleManifestValidator
{
    private static readonly HashSet<string> ValidPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "filesystem", "network", "shell", "clipboard",
        "input_device", "credentials", "system", "ui"
    };

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9A-Za-z-]*))*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$", RegexOptions.Compiled)]
    private static partial Regex SemVerRegex();

    public ManifestValidationResult Validate(ModuleManifest manifest)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(manifest.Name))
            errors.Add("Missing required field: name");

        if (string.IsNullOrWhiteSpace(manifest.DisplayName))
            errors.Add("Missing required field: displayName");

        if (string.IsNullOrWhiteSpace(manifest.Version))
            errors.Add("Missing required field: version");
        else if (!SemVerRegex().IsMatch(manifest.Version))
            errors.Add($"Invalid version format: '{manifest.Version}'. Expected SemVer (e.g. 1.0.0)");

        if (string.IsNullOrWhiteSpace(manifest.Description))
            errors.Add("Missing required field: description");

        if (string.IsNullOrWhiteSpace(manifest.Entry))
            errors.Add("Missing required field: entry");

        if (manifest.Permissions is null || manifest.Permissions.Length == 0)
            errors.Add("Missing required field: permissions");
        else
        {
            foreach (var perm in manifest.Permissions)
            {
                if (!ValidPermissions.Contains(perm))
                    errors.Add($"Invalid permission declared: '{perm}'. Valid permissions: {string.Join(", ", ValidPermissions)}");
            }
        }

        if (manifest.MinHostVersion is not null && !SemVerRegex().IsMatch(manifest.MinHostVersion))
            errors.Add($"Invalid minHostVersion format: '{manifest.MinHostVersion}'. Expected SemVer");

        return errors.Count == 0
            ? ManifestValidationResult.Success()
            : ManifestValidationResult.Failure(errors);
    }
}
