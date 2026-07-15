using System.Runtime.InteropServices;
using CodeBrix.Audio.Engine.Backends.MiniAudio.Enums;

namespace CodeBrix.Audio.Engine.Backends.MiniAudio.Structs;  //was previously: SoundFlow.Backends.MiniAudio.Structs

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct SfOpenSlConfig
{
    public OpenSlStreamType StreamType;
    public OpenSlRecordingPreset RecordingPreset;
}