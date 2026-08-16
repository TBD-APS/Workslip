using Workslip.Application.Images;
using Xunit;

namespace Workslip.Tests.Images;

public sealed class ImageUploadLimitTests
{
    [Fact]
    public void ImageUploadLimit_IsTwentyFiveMegabytes()
    {
        Assert.Equal(25, ImageService.MaxImageSizeMegabytes);
        Assert.Equal(25L * 1024L * 1024L, ImageService.MaxImageSizeBytes);
    }
}
