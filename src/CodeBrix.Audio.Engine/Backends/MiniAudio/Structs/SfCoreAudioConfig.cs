using System.Runtime.InteropServices;

namespace CodeBrix.Audio.Engine.Backends.MiniAudio.Structs;  //was previously: SoundFlow.Backends.MiniAudio.Structs

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct SfCoreAudioConfig
{
    [MarshalAs(UnmanagedType.U4)] public uint AllowNominalSampleRateChange;
}