namespace CodeBrix.Audio.Mpeg.Mp3Support; //was previously: NLayer.NAudioSupport;

internal class Mp3FrameDecompressor : CodeBrix.Audio.Wave.IMp3FrameDecompressor
{
    MpegFrameDecoder _decoder;
    Mp3FrameWrapper _frame;

    public Mp3FrameDecompressor(CodeBrix.Audio.Wave.WaveFormat waveFormat)
    {
        // we assume waveFormat was calculated from the first frame already
        OutputFormat = CodeBrix.Audio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(waveFormat.SampleRate, waveFormat.Channels);

        _decoder = new MpegFrameDecoder();
        _frame = new Mp3FrameWrapper();
    }

    public int DecompressFrame(CodeBrix.Audio.Wave.Mp3Frame frame, byte[] dest, int destOffset)
    {
        _frame.WrappedFrame = frame;
        return _decoder.DecodeFrame(_frame, dest, destOffset);
    }

    public CodeBrix.Audio.Wave.WaveFormat OutputFormat { get; private set; }

    public void SetEQ(float[] eq)
    {
        _decoder.SetEQ(eq);
    }

    public StereoMode StereoMode
    {
        get { return _decoder.StereoMode; }
        set { _decoder.StereoMode = value; }
    }

    public void Reset()
    {
        _decoder.Reset();
    }

    public void Dispose()
    {
        // no-op, since we don't have anything to do here...
    }
}
