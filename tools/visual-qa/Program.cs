using System.Text.Json;
using OpenCvSharp;

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
};

if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: visual-qa <visible.png> <hidden.png> <metadata.json> <result.json>");
    return 64;
}

var visiblePath = args[0];
var hiddenPath = args[1];
var metadataPath = args[2];
var resultPath = args[3];

var metadata = JsonSerializer.Deserialize<VisualMetadata>(await File.ReadAllTextAsync(metadataPath), jsonOptions)
    ?? throw new InvalidOperationException("Visual QA metadata could not be parsed.");

using var visible = Cv2.ImRead(visiblePath, ImreadModes.Color);
using var hidden = Cv2.ImRead(hiddenPath, ImreadModes.Color);

if (visible.Empty() || hidden.Empty())
{
    Console.Error.WriteLine("Visual QA could not read one or both screenshots.");
    return 65;
}

if (visible.Size() != hidden.Size())
{
    Console.Error.WriteLine("Visible and hidden screenshots must have identical dimensions.");
    return 66;
}

var bounds = metadata.Bounds;
var viewport = metadata.Viewport;
var clipped = bounds.X < 0 || bounds.Y < 0 ||
              bounds.X + bounds.Width > viewport.Width ||
              bounds.Y + bounds.Height > viewport.Height;

var x = Math.Clamp((int)Math.Floor(bounds.X), 0, visible.Width - 1);
var y = Math.Clamp((int)Math.Floor(bounds.Y), 0, visible.Height - 1);
var right = Math.Clamp((int)Math.Ceiling(bounds.X + bounds.Width), x + 1, visible.Width);
var bottom = Math.Clamp((int)Math.Ceiling(bounds.Y + bounds.Height), y + 1, visible.Height);
var roiRect = new Rect(x, y, right - x, bottom - y);

using var visibleRoi = new Mat(visible, roiRect);
using var hiddenRoi = new Mat(hidden, roiRect);
using var delta = new Mat();
Cv2.Absdiff(visibleRoi, hiddenRoi, delta);
using var grayDelta = new Mat();
Cv2.CvtColor(delta, grayDelta, ColorConversionCodes.BGR2GRAY);

var meanDelta = Cv2.Mean(grayDelta).Val0;
using var changedMask = new Mat();
Cv2.Threshold(grayDelta, changedMask, metadata.PixelDeltaThreshold, 255, ThresholdTypes.Binary);
var changedPixels = Cv2.CountNonZero(changedMask);
var changedRatio = changedPixels / (double)(roiRect.Width * roiRect.Height);

using var visibleGray = new Mat();
Cv2.CvtColor(visibleRoi, visibleGray, ColorConversionCodes.BGR2GRAY);
Cv2.MeanStdDev(visibleGray, out _, out var stddev);
var textureStdDev = stddev.Val0;

var contributesPixels = meanDelta >= metadata.MinMeanDelta && changedRatio >= metadata.MinChangedPixelRatio;
var pass = !clipped && contributesPixels;
var severity = pass ? "pass" : "block";
var reason = clipped
    ? "expected element bounds extend outside the viewport"
    : contributesPixels
        ? "element contributes measurable pixels inside its DOM bounds"
        : "DOM element exists but hiding it produces no meaningful visual delta";

var result = new VisualResult(
    metadata.Name,
    severity,
    reason,
    clipped,
    Math.Round(meanDelta, 3),
    Math.Round(changedRatio, 5),
    Math.Round(textureStdDev, 3),
    roiRect.X,
    roiRect.Y,
    roiRect.Width,
    roiRect.Height);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result, jsonOptions));
Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
return pass ? 0 : 2;

internal sealed record VisualMetadata(
    string Name,
    ElementBounds Bounds,
    ViewportSize Viewport,
    double MinMeanDelta = 2.0,
    double MinChangedPixelRatio = 0.015,
    double PixelDeltaThreshold = 8.0);

internal sealed record ElementBounds(double X, double Y, double Width, double Height);
internal sealed record ViewportSize(int Width, int Height);
internal sealed record VisualResult(
    string Name,
    string Severity,
    string Reason,
    bool Clipped,
    double MeanDelta,
    double ChangedPixelRatio,
    double TextureStdDev,
    int RoiX,
    int RoiY,
    int RoiWidth,
    int RoiHeight);
