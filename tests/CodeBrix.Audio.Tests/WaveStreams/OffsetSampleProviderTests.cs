using System;
using System.Linq;
using CodeBrix.Audio.Wave;
using CodeBrix.Audio.Wave.SampleProviders;
using CodeBrix.Audio.Tests.Utils;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.WaveStreams;
public class OffsetSampleProviderTests
{
    [Fact]
    public void DefaultShouldPassStraightThrough()
    {
        var source = new TestSampleProvider(32000, 1);
        var osp = new OffsetSampleProvider(source);

        var expected = new float[] { 0, 1, 2, 3, 4, 5, 6 };
        osp.AssertReadsExpected(expected);
    }

    [Fact]
    public void CanAddPreDelay()
    {
        var source = new TestSampleProvider(32000, 1) {Position = 10};
        var osp = new OffsetSampleProvider(source) {DelayBySamples = 5};

        var expected = new float[] { 0, 0, 0, 0, 0, 10, 11, 12, 13, 14, 15 };
        osp.AssertReadsExpected(expected);
    }


    [Fact]
    public void CanAddPreDelayUsingTimeSpan()
    {
        var source = new TestSampleProvider(100, 1) { Position = 10 };
        var osp = new OffsetSampleProvider(source) { DelayBy = TimeSpan.FromSeconds(1) };

        var expected = Enumerable.Range(0,100).Select(x => 0f)
                        .Concat(Enumerable.Range(10, 10).Select(x => (float)x)).ToArray();
        osp.AssertReadsExpected(expected);
    }

    [Fact]
    public void CanAddPreDelayToStereoSourceUsingTimeSpan()
    {
        var source = new TestSampleProvider(100, 2) { Position = 10 };
        var osp = new OffsetSampleProvider(source) { DelayBy = TimeSpan.FromSeconds(1) };

        var expected = Enumerable.Range(0, 200).Select(x => 0f)
                        .Concat(Enumerable.Range(10, 10).Select(x => (float)x)).ToArray();
        osp.AssertReadsExpected(expected);
    }

    [Fact]
    public void SettingPreDelayUsingTimeSpanReturnsCorrectTimeSpan()
    {
        var source = new TestSampleProvider(100, 2) { Position = 10 };
        var osp = new OffsetSampleProvider(source) { DelayBy = TimeSpan.FromSeconds(2.5) };

        Assert.Equal(2500, (int) osp.DelayBy.TotalMilliseconds);
        Assert.Equal(500, osp.DelayBySamples);
    }

    [Fact]
    public void CanSkipOver()
    {
        var source = new TestSampleProvider(32000, 1);
        var osp = new OffsetSampleProvider(source) {SkipOverSamples = 17};

        var expected = new float[] { 17,18,19,20,21,22,23,24 };
        osp.AssertReadsExpected(expected);
    }

    [Fact]
    public void CanTake()
    {
        var source = new TestSampleProvider(32000, 1);
        var osp = new OffsetSampleProvider(source) {TakeSamples = 7};

        var expected = new float[] { 0, 1, 2, 3, 4, 5, 6 };
        osp.AssertReadsExpected(expected, 10);
    }


    [Fact]
    public void CanTakeThirtySeconds()
    {
        var source = new TestSampleProvider(16000, 1);
        var osp = new OffsetSampleProvider(source) { Take = TimeSpan.FromSeconds(30) };
        var buffer = new float[16000];
        var totalRead = 0;
        while (true)
        {
            var read = osp.Read(buffer.AsSpan());
            totalRead += read;
            if (read == 0) break;
            Assert.True(totalRead <= 480000);

        }
        Assert.Equal(480000, totalRead);

    }

    [Fact]
    public void CanAddLeadOut()
    {
        var source = new TestSampleProvider(32000, 1, 10);
        var osp = new OffsetSampleProvider(source) {LeadOutSamples = 5};

        var expected = new float[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 0, 0, 0, 0 };
        osp.AssertReadsExpected(expected, 100);
        var expected2 = new float[] { };
        osp.AssertReadsExpected(expected2, 100);
    }

    [Fact]
    public void LeadOutWithoutTakeOnlyBeginsAfterSourceIsCompletelyRead()
    {
        var source = new TestSampleProvider(32000, 1, 10);
        var osp = new OffsetSampleProvider(source) { LeadOutSamples = 5 };

        var expected = new float[] { 0, 1, 2, 3, 4, 5, 6 };
        osp.AssertReadsExpected(expected, 7);
        var expected2 = new float[] { 7, 8, 9, 0, 0, 0 };
        osp.AssertReadsExpected(expected2, 6);
        var expected3 = new float[] { 0, 0 };
        osp.AssertReadsExpected(expected3, 6);
    }

    [Fact]
    public void WaveFormatIsSampeAsSource()
    {
        var source = new TestSampleProvider(32000, 1, 10);
        var osp = new OffsetSampleProvider(source);
        Assert.Equal(source.WaveFormat, osp.WaveFormat);
    }


    [Fact]
    public void MaintainsPredelayState()
    {
        var source = new TestSampleProvider(32000, 1) {Position = 10};
        var osp = new OffsetSampleProvider(source) {DelayBySamples = 10};

        var expected = new float[] {0, 0, 0, 0, 0,};
        osp.AssertReadsExpected(expected);
        var expected2 = new float[] {0, 0, 0, 0, 0,};
        osp.AssertReadsExpected(expected2);
        var expected3 = new float[] {10, 11, 12, 13, 14, 15};
        osp.AssertReadsExpected(expected3);
    }

    [Fact]
    public void CanFollowTakeWithLeadout()
    {
        var source = new TestSampleProvider(32000, 1) { Position = 10 };
        var osp = new OffsetSampleProvider(source) { TakeSamples = 10, LeadOutSamples = 5};


        var expected = new float[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 0, 0, 0, 0, 0 };
        osp.AssertReadsExpected(expected);
    }

    [Fact]
    public void MaintainsTakeState()
    {
        var source = new TestSampleProvider(32000, 1);
        var osp = new OffsetSampleProvider(source) {TakeSamples = 15};

        var expected = new float[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        osp.AssertReadsExpected(expected);
        var expected2 = new float[] { 8, 9, 10, 11, 12, 13, 14 };
        osp.AssertReadsExpected(expected2, 20);
    }

    [Fact]
    public void CantSetDelayBySamplesAfterCallingRead()
    {
        var source = new TestSampleProvider(32000, 1);
        var osp = new OffsetSampleProvider(source);
        var buffer = new float[10];
        osp.Read(buffer.AsSpan());

        Assert.Throws<InvalidOperationException>(() => osp.DelayBySamples = 4);
    }

    [Fact]
    public void CantSetLeadOutSamplesAfterCallingRead()
    {
        var source = new TestSampleProvider(32000, 1);
        var osp = new OffsetSampleProvider(source);
        var buffer = new float[10];
        osp.Read(buffer.AsSpan());

        Assert.Throws<InvalidOperationException>(() => osp.LeadOutSamples = 4);
    }

    [Fact]
    public void CantSetSkipOverSamplesAfterCallingRead()
    {
        var source = new TestSampleProvider(32000, 1);
        var osp = new OffsetSampleProvider(source);
        var buffer = new float[10];
        osp.Read(buffer.AsSpan());

        Assert.Throws<InvalidOperationException>(() => osp.SkipOverSamples = 4);
    }

    [Fact]
    public void CantSetTakeSamplesAfterCallingRead()
    {
        var source = new TestSampleProvider(32000, 1);
        var osp = new OffsetSampleProvider(source);
        var buffer = new float[10];
        osp.Read(buffer.AsSpan());

        Assert.Throws<InvalidOperationException>(() => osp.TakeSamples = 4);
    }

    [Fact]
    public void HandlesSkipOverEntireSourceCorrectly()
    {
        var source = new TestSampleProvider(32000, 1, 10);
        var osp = new OffsetSampleProvider(source);
        osp.SkipOverSamples = 20;

        var expected = new float[] { };
        osp.AssertReadsExpected(expected, 20);
    }


    [Fact]
    public void CantSetNonBlockAlignedDelayBySamples()
    {
        var source = new TestSampleProvider(32000, 2);
        var osp = new OffsetSampleProvider(source);

        var ex = Assert.Throws<ArgumentException>(() => osp.DelayBySamples = 3);
        Assert.Contains("DelayBySamples", ex.Message);
    }

    [Fact]
    public void CantSetNonBlockAlignedSkipOverSamples()
    {
        var source = new TestSampleProvider(32000, 2);
        var osp = new OffsetSampleProvider(source);

        var ex = Assert.Throws<ArgumentException>(() => osp.SkipOverSamples = 3);
        Assert.Contains("SkipOverSamples", ex.Message);
    }

    [Fact]
    public void CantSetNonBlockAlignedTakeSamples()
    {
        var source = new TestSampleProvider(32000, 2);
        var osp = new OffsetSampleProvider(source);

        var ex = Assert.Throws<ArgumentException>(() => osp.TakeSamples = 3);
        Assert.Contains("TakeSamples", ex.Message);
    }


    [Fact]
    public void CantSetNonBlockAlignedLeadOutSamples()
    {
        var source = new TestSampleProvider(32000, 2);
        var osp = new OffsetSampleProvider(source);

        var ex = Assert.Throws<ArgumentException>(() => osp.LeadOutSamples = 3);
        Assert.Contains("LeadOutSamples", ex.Message);
    }

    // TODO: Test that Read offset parameter is respected
}
