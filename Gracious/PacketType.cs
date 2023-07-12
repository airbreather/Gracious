namespace Gracious;

// serialized!
internal enum PacketType : byte
{
    StartOfStream = 0,

    EndOfRecording = 1,

    UserSpeaking = 2,

    VoiceReceived = 3,

    StartOfSendMusic = 5,

    EndOfSendMusic = 6,
}
