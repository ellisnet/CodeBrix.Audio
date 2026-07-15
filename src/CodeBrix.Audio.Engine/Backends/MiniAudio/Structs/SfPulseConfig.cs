using System.Runtime.InteropServices;

namespace CodeBrix.Audio.Engine.Backends.MiniAudio.Structs;  //was previously: SoundFlow.Backends.MiniAudio.Structs

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct SfPulseConfig
{
    public nint pStreamNamePlayback;
    public nint pStreamNameCapture;
}