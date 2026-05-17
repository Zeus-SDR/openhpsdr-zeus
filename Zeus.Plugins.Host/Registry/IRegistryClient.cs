using Zeus.Plugins.Contracts.Registry;

namespace Zeus.Plugins.Host.Registry;

/// <summary>
/// Fetches the plugin registry catalog. Default implementation reads
/// from the configured HTTPS URL; tests substitute a fake.
/// </summary>
public interface IRegistryClient
{
    /// <summary>
    /// Fetch the current catalog. Throws <see cref="RegistryFetchException"/>
    /// on network / parse failure. Implementations MAY cache the result
    /// in-memory; callers should not assume freshness past a few minutes.
    /// </summary>
    Task<RegistryCatalog> FetchAsync(CancellationToken ct);

    /// <summary>
    /// URL the client is reading from. Used by the REST layer so the
    /// frontend can surface "registry source" in the Plugins UI.
    /// </summary>
    string SourceUrl { get; }
}

public sealed class RegistryFetchException : Exception
{
    public RegistryFetchException(string message) : base(message) { }
    public RegistryFetchException(string message, Exception inner) : base(message, inner) { }
}
