using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using Android.Widget;
using CodeBrix.Audio.Codecs;
using CodeBrix.Audio.Tests.Utils;
using CodeBrix.Audio.Vorbis;

namespace CodeBrix.Audio.AndroidTests;

/// <summary>Exercises managed Core decoders without an Android audio backend or native codecs.</summary>
[Activity(Name = "com.codebrix.audio.coretests.MainActivity", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    /// <inheritdoc />
    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TextView status = new TextView(this) { Text = "Running Core decoder checks" };
        SetContentView(status);
        string directory = FilesDir.AbsolutePath;
        string resultPath = Path.Combine(directory, "results.txt");
        File.WriteAllText(resultPath, "RUNNING\n" + RuntimeInformation.FrameworkDescription + "\n");
        _ = Task.Run(() =>
        {
            void Report(string message) => File.AppendAllText(resultPath, message + "\n");
            try
            {
                Report("Core: " + typeof(VorbisPacketCodecFactory).Assembly.FullName);
                for (int size = 64; size <= 8192; size *= 2)
                {
                    Report("Starting MDCT " + size);
                    MdctReferenceChecks.Check(size);
                    Report("PASS MDCT " + size + ": cosine reference, impulse and buffer bounds");
                }
                foreach (string file in Assets.List(""))
                {
                    if (!file.EndsWith(".ogg", StringComparison.Ordinal)) continue;
                    string path = Path.Combine(directory, file);
                    using (Stream source = Assets.Open(file))
                    using (Stream destination = File.Create(path)) source.CopyTo(destination);
                    Report("Starting Vorbis packets " + file);
                    var packets = OggPacketReader.ReadPackets(path);
                    var codecPrivate = OggPacketReader.BuildXiphCodecPrivate(packets[0], packets[1], packets[2]);
                    using var decoder = new VorbisPacketCodecFactory().CreateDecoder("vorbis", codecPrivate, null);
                    Report("Decoder: " + decoder.GetType().FullName);
                    float[] decoded = new float[decoder.MaxSamplesPerPacket];
                    var packetSamples = new System.Collections.Generic.List<float>();
                    for (int i = 3; i < packets.Count; i++)
                    {
                        int produced = decoder.DecodePacket(packets[i], decoded);
                        for (int j = 0; j < produced; j++) packetSamples.Add(decoded[j]);
                    }
                    int count = packetSamples.Count;
                    if (count == 0) throw new InvalidOperationException("No decoded audio");
                    using VorbisReader streamReader = new VorbisReader(path);
                    float[] buffer = new float[4096];
                    int position = 0;
                    int read;
                    while ((read = streamReader.ReadSamples(buffer)) > 0)
                    {
                        for (int i = 0; i < read; i++)
                        {
                            if (position >= packetSamples.Count || !float.IsFinite(buffer[i]) || buffer[i] != packetSamples[position++])
                                throw new InvalidOperationException("Vorbis packet and Ogg stream samples differ.");
                        }
                    }
                    if (position == 0 || count - position > 2048 * decoder.Channels)
                        throw new InvalidOperationException("Unexpected Vorbis stream length.");
                    Report("PASS " + file + ": " + count + " samples");
                }
                Report("PASS ALL");
            }
            catch (Exception error)
            {
                Report("FAIL " + error);
            }
            RunOnUiThread(() => status.Text = File.ReadAllText(resultPath));
        });
    }

}
