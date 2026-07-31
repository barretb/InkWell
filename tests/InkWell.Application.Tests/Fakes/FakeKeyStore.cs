using InkWell.Application.Abstractions;

namespace InkWell.Application.Tests.Fakes;

/// <summary>
/// A key store that hands out one fixed key for the lifetime of a test, so a test database opens
/// and reopens with the same cipher key without involving the platform.
/// </summary>
public sealed class FakeKeyStore : IKeyStore
{
    private string? _key;

    /// <summary>Creates a store that will mint a random key on first use.</summary>
    public FakeKeyStore()
    {
    }

    /// <summary>Creates a store that always returns <paramref name="key"/>.</summary>
    public FakeKeyStore(string key) => _key = key;

    /// <summary>Whether a key is currently held (false after <see cref="DeleteKeyAsync"/>).</summary>
    public bool HasKey => _key is not null;

    /// <inheritdoc />
    public Task<string> GetOrCreateKeyAsync(CancellationToken cancellationToken = default)
    {
        _key ??= Convert.ToHexString(Guid.NewGuid().ToByteArray());
        return Task.FromResult(_key);
    }

    /// <inheritdoc />
    public Task DeleteKeyAsync(CancellationToken cancellationToken = default)
    {
        _key = null;
        return Task.CompletedTask;
    }
}
