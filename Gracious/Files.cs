/*
This file is part of Gracious.
Copyright (C) 2023 Joe Amenta

Gracious is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

Gracious is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License along with Gracious. If not, see <https://www.gnu.org/licenses/>.
*/
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
