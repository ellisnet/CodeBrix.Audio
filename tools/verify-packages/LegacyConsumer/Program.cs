using CodeBrix.Audio.Engine.Backends.MiniAudio;
using CodeBrix.Audio.Engine.Structs;
using CodeBrix.Audio.Wave;

// Build this ONCE against the old desktop package. Replace its managed dependency DLLs
// with those from Core and run the unchanged executable again to verify binary binding.
using var output = new MemoryStream();
var samples = Enumerable.Range(0, 480).Select(i => (float)(0.1 * Math.Sin(i * 0.07))).ToArray();
var factory = new MiniAudioCodecFactory();
using (var encoder = factory.CreateEncoder(output, "wav", AudioFormat.DvdHq))
    if (encoder.Encode(samples) != samples.Length) throw new Exception("WAV encoding failed.");
output.Position = 0;
using (var decoder = factory.CreateDecoder(output, "wav", AudioFormat.DvdHq))
{
    var decoded = new float[samples.Length];
    if (decoder.Decode(decoded) != samples.Length || !samples.SequenceEqual(decoded))
        throw new Exception("WAV decoding differs.");
}
SharedAudioOutput.Configure(48000);
if (SharedAudioOutput.SampleRate != 48000 || SharedAudioOutput.IsRunning)
    throw new Exception("Desktop startup behavior changed.");
SharedAudioOutput.Shutdown();
Console.WriteLine($"Legacy consumer passed using {typeof(SharedAudioOutput).Assembly.FullName} and {typeof(MiniAudioCodecFactory).Assembly.FullName}");
