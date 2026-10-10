namespace SnapGit.Core.Imaging;

public sealed record BatchOptions(
    string OutputFolder,
    ImageFileFormat Format = ImageFileFormat.Png,
    int JpegQuality = 90,
    int? MaxWidth = null,
    int? MaxHeight = null,
    bool Overwrite = false);

public sealed record BatchResult(IReadOnlyList<string> Converted, IReadOnlyList<(string File, string Error)> Failed);

/// <summary>Converts / resizes many images in one go. The loader decides how a file becomes a bitmap (so .snapgit projects can be flattened by the editor layer).</summary>
public static class BatchConverter
{
    public static BatchResult Run(IReadOnlyList<string> files, BatchOptions options, Func<string, Bitmap> loader,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(options.OutputFolder);
        var converted = new List<string>();
        var failed = new List<(string, string)>();

        for (var i = 0; i < files.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var file = files[i];
            try
            {
                using var source = loader(file);
                using var resized = options.MaxWidth is null && options.MaxHeight is null
                    ? null
                    : ImageOps.ScaleToFit(source, options.MaxWidth ?? int.MaxValue, options.MaxHeight ?? int.MaxValue);

                var target = Path.Combine(options.OutputFolder, Path.GetFileNameWithoutExtension(file) + ImageEncoder.GetExtension(options.Format));
                if (!options.Overwrite)
                    for (var n = 2; File.Exists(target); n++)
                        target = Path.Combine(options.OutputFolder, $"{Path.GetFileNameWithoutExtension(file)} ({n}){ImageEncoder.GetExtension(options.Format)}");

                ImageEncoder.Save(resized ?? source, target, options.Format, options.JpegQuality);
                converted.Add(target);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed.Add((file, ex.Message));
            }
            progress?.Report((i + 1, files.Count));
        }
        return new BatchResult(converted, failed);
    }
}
