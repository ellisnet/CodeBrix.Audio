using System.Runtime.InteropServices;
using CodeBrix.Audio.Engine.Backends.MiniAudio.Enums;

namespace CodeBrix.Audio.Engine.Backends.MiniAudio.Structs;  //was previously: SoundFlow.Backends.MiniAudio.Structs

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct SfAAudioConfig
{
    public AAudioUsage Usage;
    public AAudioContentType ContentType;
    public AAudioInputPreset InputPreset;
    public AAudioAllowedCapturePolicy AllowedCapturePolicy;
}