using System.Runtime.InteropServices;
using CodeBrix.Audio.Engine.Backends.MiniAudio.Enums;

namespace CodeBrix.Audio.Engine.Backends.MiniAudio.Structs;  //was previously: SoundFlow.Backends.MiniAudio.Structs

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct SfWasapiConfig
{
    public WasapiUsage Usage;
    [MarshalAs(UnmanagedType.U1)] public bool NoAutoConvertSRC;
    [MarshalAs(UnmanagedType.U1)] public bool NoDefaultQualitySRC;
    [MarshalAs(UnmanagedType.U1)] public bool NoAutoStreamRouting;
    [MarshalAs(UnmanagedType.U1)] public bool NoHardwareOffloading;
}