/*
This file is part of Gracious.
Copyright (C) 2023 Joe Amenta

Gracious is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

Gracious is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License along with Gracious. If not, see <https://www.gnu.org/licenses/>.
*/
using System.IO.Compression;
using System.Runtime.InteropServices;
using DSharpPlus.Entities;

namespace Gracious;

internal static class GraciousFinalize
{
    public static async Task FinalizeAsync(DiscordUser botUser, string inputPath, string outputTempFolderPath, string outputFilePath)
    {
        // TODO: 2-pass isn't the fastest, but it probably won't be slow enough to matter.
        Dictionary<uint, string> ssrcMap = await GetStreamMappingAsync(inputPath);

        FileStream inputFile;
        try
        {
            inputFile = Files.OpenForFullAsyncRead(inputPath);
        }
        catch (IOException)
        {
            throw new SessionDoesNotExistException();
        }

        await using var _ = inputFile;
        SplitPcmOutputs? splitOutputs = null;

        Directory.CreateDirectory(outputTempFolderPath);
        await foreach (Packet packet in PacketReader.ReadAsync(inputFile))
        {
            switch (packet.Type)
            {
                case PacketType.StartOfStream:
                    if (splitOutputs is not null)
                    {
                        throw new InvalidDataException("StartOfStream found multiple times");
                    }

                    StartOfStreamPacketData.ReadFromBuffer(packet.Payload.Span, out long ticksPerSecond);
                    splitOutputs = new(botUser, outputTempFolderPath, packet.StartTimestamp, ticksPerSecond, ssrcMap);
                    break;

                case PacketType.EndOfRecording:
                case PacketType.VoiceReceived:
                case PacketType.StartOfSendMusic:
                    if (splitOutputs is null)
                    {
                        throw new InvalidDataException("StartOfStream should be the first packet we see.");
                    }

                    splitOutputs.Next(in packet);
                    break;

                case PacketType.UserSpeaking:
                case PacketType.EndOfSendMusic:
                    break;

                default:
                    throw new InvalidDataException($"Unrecognized packet type: {packet.Type}");
            }
        }

        await (splitOutputs?.DisposeAsync() ?? ValueTask.CompletedTask);

        await using FileStream outputFile = Files.CreateAsync(outputFilePath);
        using ZipArchive outputZipArchive = new(outputFile, ZipArchiveMode.Create, leaveOpen: true);
        foreach (string filePath in Directory.EnumerateFiles(outputTempFolderPath))
        {
            ZipArchiveEntry entry = outputZipArchive.CreateEntry(Path.GetRelativePath(outputTempFolderPath, filePath), CompressionLevel.NoCompression);
            if (!OperatingSystem.IsWindows())
            {
                entry.ExternalAttributes |= (Convert.ToInt32("644", 8) << 16);
            }

            await using FileStream inputChildFile = Files.OpenForFullAsyncRead(filePath);
            await using Stream outputEntry = entry.Open();
            await inputChildFile.CopyToAsync(outputEntry);
        }

        await RetryLoop.RunUntilTimeout(() => Directory.Delete(outputTempFolderPath, true), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50));
    }

    private static async ValueTask<Dictionary<uint, string>> GetStreamMappingAsync(string inputPath)
    {
        FileStream inputFile;
        try
        {
            inputFile = Files.OpenForFullAsyncRead(inputPath);
        }
        catch (IOException)
        {
            throw new SessionDoesNotExistException();
        }

        await using var __ = inputFile;
        Dictionary<uint, string> map = new();
        await foreach (Packet packet in PacketReader.ReadAsync(inputFile))
        {
            switch (packet.Type)
            {
                case PacketType.UserSpeaking:
                    UserSpeakingPacketData.ReadFromBuffer(packet.Payload.Span, out uint ssrc, out string usernameWithDiscriminator);
                    CollectionsMarshal.GetValueRefOrAddDefault(map, ssrc, out _) = usernameWithDiscriminator;
                    break;
            }
        }

        return map;
    }
}
