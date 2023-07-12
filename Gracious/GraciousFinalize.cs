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
