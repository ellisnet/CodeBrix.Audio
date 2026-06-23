using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using CodeBrix.Audio.Wave;
using CodeBrix.Audio.Tests.Utils;
using System.IO;

namespace CodeBrix.Audio.Tests.WaveStreams;
public class WaveChannel32Tests
{
    [Fact]
    public void CanCreateWavFileFromWaveChannel32()
    {
        string inFile = @"F:\Recording\wav\pcm\16bit mono 8kHz.wav";
        string outFile = @"F:\Recording\wav\pcm\32bit stereo 8kHz.wav";
        if (!File.Exists(inFile))
        {
            Assert.Skip("Input test file not found");
        }
        var audio32 = new WaveChannel32(new WaveFileReader(inFile));
        audio32.PadWithZeroes = false;
        WaveFileWriter.CreateWaveFile(outFile, audio32);
    }
}
