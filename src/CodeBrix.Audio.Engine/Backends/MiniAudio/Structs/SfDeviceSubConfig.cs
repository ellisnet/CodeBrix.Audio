using System.Runtime.InteropServices;
using CodeBrix.Audio.Engine.Backends.MiniAudio.Enums;
using CodeBrix.Audio.Engine.Enums;

namespace CodeBrix.Audio.Engine.Backends.MiniAudio.Structs;  //was previously: SoundFlow.Backends.MiniAudio.Structs

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct SfDeviceSubConfig
{
    public SampleFormat Format;
    public uint Channels;
    public nint pDeviceID;
    public ShareMode ShareMode;
}