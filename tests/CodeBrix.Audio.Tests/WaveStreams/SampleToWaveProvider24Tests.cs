using System;
using System.IO;
using CodeBrix.Audio.Wave;
using CodeBrix.Audio.Wave.SampleProviders;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.WaveStreams;
public class SampleToWaveProvider24Tests
{
    [Fact]
    public void ConvertAFile()
    {
        const string input = @"C:\Users\Mark\Downloads\Region-1.wav";
        if (!File.Exists(input)) Assert.Skip("Test file not found");
        using (var reader = new WaveFileReader(input))
        {
            var sp = reader.ToSampleProvider();
            var wp24 = new SampleToWaveProvider24(sp);
            WaveFileWriter.CreateWaveFile(@"C:\Users\Mark\Downloads\Region1-24.wav", wp24);
        }
    }
}
