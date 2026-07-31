using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Engine.Abstracts;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Engine.Structs;

namespace CodeBrix.Audio.Engine.Tests;

/// <summary>
/// A codec that decodes normally but reports a Length of 0, so the providers take their
/// length-unknown fallback path.
/// </summary>
/// <remarks>
/// That path is not exotic - the native decoder reports no length whenever it runs through read
/// callbacks rather than from memory (see the re-vendor checklist), and a managed decoder reports
/// none for a source whose duration is not known up front. It is simply awkward to reach on
/// demand, which is why it went untested and wrong for so long. Forcing it with a wrapper keeps
/// the test device-less and independent of which real format happens to report what.
/// </remarks>
internal sealed class LengthlessDecoder : ISoundDecoder
{
    private readonly ISoundDecoder inner;

    public LengthlessDecoder(ISoundDecoder inner) => this.inner = inner;

    /// <summary>Always 0 - the point of this wrapper.</summary>
    public int Length => 0;

    public bool IsDisposed => inner.IsDisposed;

    public SampleFormat SampleFormat => inner.SampleFormat;

    public int Channels => inner.Channels;

    public int SampleRate => inner.SampleRate;

    public event EventHandler<EventArgs> EndOfStreamReached
    {
        add => inner.EndOfStreamReached += value;
        remove => inner.EndOfStreamReached -= value;
    }

    public bool Seek(int offset) => inner.Seek(offset);

    public int Decode(Span<float> samples) => inner.Decode(samples);

    public void Dispose() => inner.Dispose();
}

/// <summary>
/// Registers <see cref="LengthlessDecoder" /> above the built-in factory for one format id.
/// </summary>
internal sealed class LengthlessCodecFactory : ICodecFactory
{
    private readonly ICodecFactory inner;
    private readonly string formatId;

    public LengthlessCodecFactory(ICodecFactory inner, string formatId)
    {
        this.inner = inner;
        this.formatId = formatId;
    }

    public string FactoryId => "CodeBrix.Audio.Tests.Lengthless";

    public IReadOnlyCollection<string> SupportedFormatIds => new[] { formatId };

    /// <summary>Above the built-in factory's 0, so this one is asked first.</summary>
    public int Priority => 100;

    public ISoundDecoder CreateDecoder(Stream stream, string id, AudioFormat format)
    {
        var decoder = inner.CreateDecoder(stream, id, format);

        return decoder == null ? null : new LengthlessDecoder(decoder);
    }

    public ISoundDecoder TryCreateDecoder(Stream stream, out AudioFormat detectedFormat,
        AudioFormat? hintFormat = null)
    {
        var decoder = inner.TryCreateDecoder(stream, out detectedFormat, hintFormat);

        return decoder == null ? null : new LengthlessDecoder(decoder);
    }

    public ISoundEncoder CreateEncoder(Stream stream, string id, AudioFormat format) => null;
}
