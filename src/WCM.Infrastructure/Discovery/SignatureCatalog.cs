using System.Text.Json;
using System.Text.Json.Serialization;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Infrastructure.FileSystem;

namespace WCM.Infrastructure.Discovery;

/// <summary>Loads the local signature file. Anything unexpected fails closed.</summary>
public sealed class SignatureCatalog(string filePath) : ISignatureCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static SignatureCatalog CreateDefault() =>
        new(Path.Combine(AppContext.BaseDirectory, "Rules", "application-signatures.json"));

    public IReadOnlyList<KnownApplicationSignature> Load()
    {
        CatalogueFile file;
        try
        {
            file = JsonSerializer.Deserialize<CatalogueFile>(File.ReadAllText(filePath), Options)
                ?? throw new InvalidDataException("The signature file is empty.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"The signature file can't be used: {filePath}", ex);
        }

        return file.Signatures.Select(Validate).ToList();
    }

    private static KnownApplicationSignature Validate(KnownApplicationSignature signature)
    {
        if (string.IsNullOrWhiteSpace(signature.SignatureId)
            || string.IsNullOrWhiteSpace(signature.SignatureVersion)
            || string.IsNullOrWhiteSpace(signature.NameContains)
            || signature.Paths is not { Count: > 0 })
        {
            throw new InvalidDataException($"Signature \"{signature.SignatureId}\" is incomplete.");
        }

        return signature with { Paths = signature.Paths.Select(path => Expand(signature.SignatureId, path)).ToList() };
    }

    // Every path must name a folder below a known location, never the location itself.
    private static string Expand(string id, string path)
    {
        var separator = path.StartsWith('%') ? path.IndexOf('\\', StringComparison.Ordinal) : -1;
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (separator < 0 || separator == path.Length - 1 || expanded.Contains('%', StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Signature \"{id}\" has an invalid path: {path}");
        }

        return PathNormalizer.Normalize(expanded);
    }

    private sealed record CatalogueFile(string CatalogueVersion, IReadOnlyList<KnownApplicationSignature> Signatures);
}
