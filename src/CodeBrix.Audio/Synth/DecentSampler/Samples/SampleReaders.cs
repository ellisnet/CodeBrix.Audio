using System;
using System.IO;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Synth.DecentSampler.Samples;

// Opens the audio formats the Decent Sampler format documents, from a stream rather than a path,
// because a sample may live inside a .dslibrary where there is no file to name.
//
// Shared by the in-memory decode and by the streaming source so both engines agree, byte for byte,
// about which reader handles which extension.
internal static class SampleReaders
{
    // WAV, AIFF and FLAC are the three formats the guide names. AIFF has no entry in the shared reader
    // registry - nothing else in the library reads it by file name - so it is opened directly; anything
    // else falls back to the registry, which is what lets a library get away with .ogg or .mp3.
    public static WaveStream Open(Stream stream, string fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

        switch (extension)
        {
            case ".wav":
            case ".wave":
                return new WaveFileReader(stream);

            case ".aif":
            case ".aiff":
            case ".aifc":
                return new AiffFileReader(stream);

            case ".flac":
                return new FlacFileReader(stream);

            default:
                return AudioFileReaderRegistry.GetFactory(extension)(stream);
        }
    }

    // How many channels a decoded source presents. Anything past stereo is folded to stereo, exactly as
    // SfzSampleData does, because a zone has no use for surround stems.
    public static int TargetChannelCount(int sourceChannels) => Math.Min(Math.Max(1, sourceChannels), 2);
}
