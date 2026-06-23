using CodeBrix.Audio.Wave;
using System;

namespace CodeBrix.Audio.Mpeg.Mp3Support; //was previously: NLayer.NAudioSupport;

class Mp3FrameWrapper : IMpegFrame
{
    Mp3Frame _frame;

    internal Mp3Frame WrappedFrame
    {
        set
        {
            _frame = value;
            Reset();
        }
    }

    public int SampleRate
    {
        get { return _frame.SampleRate; }
    }

    public int SampleRateIndex
    {
        // we have to manually parse this out
        get
        {
            // sri is in bits 10 & 11 of the sync DWORD...  pull them out
            return (_frame.RawData[2] >> 2) & 3;
        }
    }

    public int FrameLength
    {
        get { return _frame.FrameLength; }
    }

    public int BitRate
    {
        get { return _frame.BitRate; }
    }

    public MpegVersion Version
    {
        get
        {
            switch (_frame.MpegVersion)
            {
                case CodeBrix.Audio.Wave.MpegVersion.Version1: return MpegVersion.Version1;
                case CodeBrix.Audio.Wave.MpegVersion.Version2: return MpegVersion.Version2;
                case CodeBrix.Audio.Wave.MpegVersion.Version25: return MpegVersion.Version25;
            }
            return MpegVersion.Unknown;
        }
    }

    public MpegLayer Layer
    {
        get
        {
            switch (_frame.MpegLayer)
            {
                case CodeBrix.Audio.Wave.MpegLayer.Layer1: return MpegLayer.LayerI;
                case CodeBrix.Audio.Wave.MpegLayer.Layer2: return MpegLayer.LayerII;
                case CodeBrix.Audio.Wave.MpegLayer.Layer3: return MpegLayer.LayerIII;
            }
            return MpegLayer.Unknown;
        }
    }

    public MpegChannelMode ChannelMode
    {
        get
        {
            switch (_frame.ChannelMode)
            {
                case CodeBrix.Audio.Wave.ChannelMode.Stereo: return MpegChannelMode.Stereo;
                case CodeBrix.Audio.Wave.ChannelMode.JointStereo: return MpegChannelMode.JointStereo;
                case CodeBrix.Audio.Wave.ChannelMode.DualChannel: return MpegChannelMode.DualChannel;
                case CodeBrix.Audio.Wave.ChannelMode.Mono: return MpegChannelMode.Mono;
            }
            return (MpegChannelMode)(-1);
        }
    }

    public int ChannelModeExtension
    {
        get { return _frame.ChannelExtension; }
    }

    public int SampleCount
    {
        get { return _frame.SampleCount; }
    }

    public int BitRateIndex
    {
        get { return _frame.BitRateIndex; }
    }

    public bool IsCopyrighted
    {
        get { return _frame.Copyright; }
    }

    public bool HasCrc
    {
        get { return _frame.CrcPresent; }
    }

    // we assume everything is good here since CodeBrix.Audio should've already caught any errors
    public bool IsCorrupted
    {
        get { return false; }
    }

    int _readOffset, _bitsRead;
    ulong _bitBucket;

    public void Reset()
    {
        if (_frame != null)
        {
            _readOffset = (_frame.CrcPresent ? 2 : 0) + 4;
        }
        _bitsRead = 0;
    }

    public int ReadBits(int bitCount)
    {
        if (bitCount < 1 || bitCount > 32) throw new ArgumentOutOfRangeException("bitCount");
        while (_bitsRead < bitCount)
        {
            if (_readOffset == _frame.FrameLength) throw new System.IO.EndOfStreamException();

            var b = _frame.RawData[_readOffset++];
            _bitBucket <<= 8;
            _bitBucket |= (byte)(b & 0xFF);
            _bitsRead += 8;
        }

        var temp = (int)((_bitBucket >> (_bitsRead - bitCount)) & ((1UL << bitCount) - 1));
        _bitsRead -= bitCount;
        return temp;
    }
}
