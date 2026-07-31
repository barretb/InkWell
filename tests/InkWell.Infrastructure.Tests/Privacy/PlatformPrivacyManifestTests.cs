using System.Xml.Linq;

namespace InkWell.Infrastructure.Tests.Privacy;

/// <summary>
/// FR-017 and SC-002 enforced at the platform-manifest level: InkWell must not even be *able* to
/// transmit user content.
/// </summary>
/// <remarks>
/// Code review can miss a re-added permission; this cannot. If someone restores the MAUI template's
/// default INTERNET permission or network-client entitlement, the privacy guarantee in the spec
/// quietly becomes an assertion about intent rather than capability, and this test fails.
/// </remarks>
public class PlatformPrivacyManifestTests
{
    [Fact]
    public void Android_does_not_request_internet_access()
    {
        XDocument manifest = XDocument.Load(PathTo("Platforms", "Android", "AndroidManifest.xml"));
        XNamespace android = "http://schemas.android.com/apk/res/android";

        string[] permissions = [.. manifest
            .Descendants("uses-permission")
            .Select(e => (string?)e.Attribute(android + "name") ?? string.Empty)];

        Assert.DoesNotContain("android.permission.INTERNET", permissions);
        Assert.DoesNotContain("android.permission.ACCESS_NETWORK_STATE", permissions);
    }

    [Fact]
    public void Android_does_not_back_up_the_encrypted_database()
    {
        // research.md §2: a restored database whose Keystore key did not travel with it is
        // unreadable, so backup would turn a device swap into apparent data loss.
        XDocument manifest = XDocument.Load(PathTo("Platforms", "Android", "AndroidManifest.xml"));
        XNamespace android = "http://schemas.android.com/apk/res/android";

        string? allowBackup = (string?)manifest.Descendants("application").Single().Attribute(android + "allowBackup");

        Assert.Equal("false", allowBackup);
    }

    [Theory]
    [InlineData("iOS")]
    [InlineData("MacCatalyst")]
    public void Apple_entitlements_grant_keychain_but_not_network_access(string platform)
    {
        string plist = File.ReadAllText(PathTo("Platforms", platform, "Entitlements.plist"));

        Assert.Contains("keychain-access-groups", plist, StringComparison.Ordinal);
        Assert.DoesNotContain("com.apple.security.network.client", plist, StringComparison.Ordinal);
        Assert.DoesNotContain("com.apple.security.network.server", plist, StringComparison.Ordinal);
    }

    [Fact]
    public void MacCatalyst_can_write_only_to_a_location_the_writer_chose()
    {
        // The single outbound path allowed by FR-017/FR-018.
        string plist = File.ReadAllText(PathTo("Platforms", "MacCatalyst", "Entitlements.plist"));

        Assert.Contains("com.apple.security.files.user-selected.read-write", plist, StringComparison.Ordinal);
        Assert.DoesNotContain("com.apple.security.files.downloads.read-write", plist, StringComparison.Ordinal);
    }

    private static string PathTo(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InkWell.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory.FullName, "src", "InkWell.Maui", .. segments]);
    }
}
