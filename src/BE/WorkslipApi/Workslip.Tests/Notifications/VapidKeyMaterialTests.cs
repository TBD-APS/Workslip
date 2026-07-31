using Microsoft.Extensions.Options;
using Workslip.Infrastructure.Configuration;
using Workslip.Infrastructure.Notifications;
using Xunit;

namespace Workslip.Tests.Notifications;

public sealed class VapidKeyMaterialTests
{
    private const string PrivateScalarOne =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAE";
    private const string ExpectedPublicKey =
        "BGsX0fLhLEJH-Lzm5WOkQPJ3A32BLeszoPShOUXYmMKWT-NC4v4af5uO5-tKfA-eFivOM1drMV7Oy7ZAaDe_UfU";

    [Fact]
    public void Constructor_DerivesPublicKeyFromPrivateKey()
    {
        var material = CreateMaterial(new VapidOptions
        {
            PrivateKey = PrivateScalarOne
        });

        Assert.Equal(ExpectedPublicKey, material.PublicKey);
        Assert.Equal(PrivateScalarOne, material.PrivateKey);
    }

    [Fact]
    public void Constructor_RejectsInvalidPrivateKeyLength()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateMaterial(new VapidOptions { PrivateKey = "AQ" }));

        Assert.Contains("32-byte P-256", exception.Message, StringComparison.Ordinal);
    }

    private static VapidKeyMaterial CreateMaterial(VapidOptions options) =>
        new(Options.Create(options));
}
