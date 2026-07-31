using System.Collections.Concurrent;
using InkWell.Application.Abstractions;

namespace InkWell.Application.Tests.Fakes;

/// <summary>
/// An in-memory stand-in for the device Keychain/Keystore. Lets the real key-bootstrap logic and
/// the real SQLCipher database be exercised in tests without touching platform secure storage.
/// </summary>
public sealed class InMemorySecureStore : ISecureStore
{
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>When true, every call throws, simulating a missing Keychain entitlement.</summary>
    public bool SimulateUnavailable { get; set; }

    /// <summary>How many distinct keys the store currently holds.</summary>
    public int Count => _values.Count;

    /// <inheritdoc />
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        return Task.FromResult(_values.TryGetValue(key, out string? value) ? value : null);
    }

    /// <inheritdoc />
    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        _values[key] = value;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _values.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private void ThrowIfUnavailable()
    {
        if (SimulateUnavailable)
        {
            throw new KeyStoreUnavailableException("Simulated secure storage failure.");
        }
    }
}
