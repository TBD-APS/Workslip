using Microsoft.Extensions.Options;
using Workslip.Application.Notifications;
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
    public void Constructor_SetsIsConfiguredFalse_WhenPrivateKeyLengthIsInvalid()
    {
        var material = CreateMaterial(new VapidOptions { PrivateKey = "AQ" });

        Assert.False(material.IsConfigured);
        Assert.Equal(string.Empty, material.PublicKey);
        Assert.Equal(string.Empty, material.PrivateKey);
    }

    private static VapidKeyMaterial CreateMaterial(VapidOptions options) =>
        new(Options.Create(options));
}
