using Workslip.Application.Jobs;

namespace Workslip.Tests.Jobs;

public sealed class JobRejectionCategoryCodecTests
{
    [Fact]
    public void Decode_KnownStructuredCategory_ReturnsCategoryAndCleanNote()
    {
        var decoded = JobRejectionCategoryCodec.Decode(
            "[workslip-rejection:missing_photo_documentation] Tilføj billeder af installationen.");

        Assert.Equal("missing_photo_documentation", decoded.Category);
        Assert.Equal("Tilføj billeder af installationen.", decoded.Note);
    }

    [Fact]
    public void Decode_HistoricPlainText_KeepsNoteAndLeavesCategoryEmpty()
    {
        var decoded = JobRejectionCategoryCodec.Decode("  Mangler dokumentation  ");

        Assert.Null(decoded.Category);
        Assert.Equal("Mangler dokumentation", decoded.Note);
    }

    [Fact]
    public void Decode_UnknownMarker_DoesNotPromoteUnknownCategory()
    {
        var decoded = JobRejectionCategoryCodec.Decode(
            "[workslip-rejection:not_a_real_category] Ret sagen.");

        Assert.Null(decoded.Category);
        Assert.Equal("Ret sagen.", decoded.Note);
    }
}