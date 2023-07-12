using ZstdNet;

namespace Gracious;

internal static class Files
{
    private static readonly FileStreamOptions MyReadEntireFileStreamOptions = new()
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.Read,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
    };

    private static readonly FileStreamOptions MyWriteFileStreamOptions = new()
    {
        Mode = FileMode.Create,
        Access = FileAccess.ReadWrite,
        Share = FileShare.None,
        Options = FileOptions.Asynchronous,
    };

    public static FileStream OpenForFullAsyncRead(string path) => new(path, MyReadEntireFileStreamOptions);

    public static FileStream CreateAsync(string path) => new(path, MyWriteFileStreamOptions);

    public static DecompressionStream OpenCompressedForFullAsyncRead(string path) => new(OpenForFullAsyncRead(path));
}
