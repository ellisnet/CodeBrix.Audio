using System;
using System.Text;

namespace CodeBrix.Audio.Wave; //was previously: NAudio.Wave;

/// <summary>
/// Represents a Xing VBR header
/// </summary>
public class XingHeader
{
    [Flags]
    enum XingHeaderOptions
    {
        Frames = 1,
        Bytes = 2,
        Toc = 4,
        VbrScale = 8
    }

    // The encoder extension that follows the Xing/Info fields. Its layout, from the first byte
    // of the nine-character encoder string:
    //     0..8    encoder version, ASCII ("LAME3.100", "Lavc60.31", ...)
    //     9       info tag revision (high nibble) and VBR method (low nibble)
    //     10      lowpass filter value
    //     11..20  replay gain and encoding flags
    //     21..23  encoder delay (12 bits) then encoder padding (12 bits)
    //     24..35  misc, mp3gain, preset, music length, two CRCs
    private const int EncoderTagLength = 9;
    private const int DelayPaddingOffset = 21;
    private const int MinimumEncoderTagBytes = DelayPaddingOffset + 3;
    private const int MaximumInfoTagRevision = 1;
    private const int ImplausibleEncoderDelay = 3000;

    private static int[] sr_table = { 44100, 48000, 32000, 99999 };
    private int vbrScale = -1;
    private int startOffset;
    private int endOffset;

    private int tocOffset = -1;
    private int framesOffset = -1;
    private int bytesOffset = -1;
    private Mp3Frame frame;
    private string encoderTag;
    private int encoderDelay;
    private int encoderPadding;

    private static int ReadBigEndian(byte[] buffer, int offset)
    {
        int x;
        // big endian extract
        x = buffer[offset+0];
        x <<= 8;
        x |= buffer[offset+1];
        x <<= 8;
        x |= buffer[offset+2];
        x <<= 8;
        x |= buffer[offset+3];

        return x;
    }

    private void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        byte[] littleEndian = BitConverter.GetBytes(value);
        for (int n = 0; n < 4; n++)
        {
            buffer[offset + 3 - n] = littleEndian[n];
        }
    }

    /// <summary>
    /// Load Xing Header
    /// </summary>
    /// <param name="frame">Frame</param>
    /// <returns>Xing Header</returns>
    public static XingHeader LoadXingHeader(Mp3Frame frame)
    {
        XingHeader xingHeader = new XingHeader();
        xingHeader.frame = frame;
        int offset = 0;

        if (frame.MpegVersion == MpegVersion.Version1)
        {
            if (frame.ChannelMode != ChannelMode.Mono)
                offset = 32 + 4;
            else
                offset = 17 + 4;
        }
        else if (frame.MpegVersion == MpegVersion.Version2)
        {
            if (frame.ChannelMode != ChannelMode.Mono)
                offset = 17 + 4;
            else
                offset = 9 + 4;
        }
        else
        {
            return null;
            // throw new FormatException("Unsupported MPEG Version");
        }

        if ((frame.RawData[offset + 0] == 'X') &&
            (frame.RawData[offset + 1] == 'i') &&
            (frame.RawData[offset + 2] == 'n') &&
            (frame.RawData[offset + 3] == 'g'))
        {
            xingHeader.startOffset = offset;
            offset += 4;
        }
        else if ((frame.RawData[offset + 0] == 'I') &&
                 (frame.RawData[offset + 1] == 'n') &&
                 (frame.RawData[offset + 2] == 'f') &&
                 (frame.RawData[offset + 3] == 'o'))
        {
            xingHeader.startOffset = offset;
            offset += 4;
        }
        else
        {
            return null;
        }

        XingHeaderOptions flags = (XingHeaderOptions)ReadBigEndian(frame.RawData, offset);
        offset += 4;

        if ((flags & XingHeaderOptions.Frames) != 0)
        {
            xingHeader.framesOffset = offset;
            offset += 4;
        }
        if ((flags & XingHeaderOptions.Bytes) != 0)
        {
            xingHeader.bytesOffset = offset;
            offset += 4;
        }
        if ((flags & XingHeaderOptions.Toc) != 0)
        {
            xingHeader.tocOffset = offset;
            offset += 100;
        }
        if ((flags & XingHeaderOptions.VbrScale) != 0)
        {
            xingHeader.vbrScale = ReadBigEndian(frame.RawData, offset);
            offset += 4;
        }
        xingHeader.endOffset = offset;
        xingHeader.ReadEncoderTag();
        return xingHeader;
    }

    /// <summary>
    /// Reads the LAME-style encoder extension that sits immediately after the Xing/Info
    /// fields, if this frame has room for one and it looks like a real tag. Silence is the
    /// correct outcome for a frame without one: plenty of Xing headers carry nothing here.
    /// </summary>
    private void ReadEncoderTag()
    {
        byte[] data = frame.RawData;
        if (data == null || endOffset + MinimumEncoderTagBytes > data.Length) { return; }

        for (int n = 0; n < EncoderTagLength; n++)
        {
            byte c = data[endOffset + n];
            if (c < 0x20 || c > 0x7E) { return; }
        }

        // Revisions beyond 1 are not defined, and a byte that decodes to one is the cheapest
        // evidence that what follows the encoder name really is the documented layout.
        int revision = data[endOffset + EncoderTagLength] >> 4;
        if (revision > MaximumInfoTagRevision) { return; }

        int b0 = data[endOffset + DelayPaddingOffset];
        int b1 = data[endOffset + DelayPaddingOffset + 1];
        int b2 = data[endOffset + DelayPaddingOffset + 2];
        int delay = (b0 << 4) | (b1 >> 4);
        int padding = ((b1 & 0x0F) << 8) | b2;

        // A delay of more than a couple of frames is not an encoder delay, it is a
        // coincidence in a frame that never carried an encoder tag at all.
        if (delay > ImplausibleEncoderDelay) { return; }

        encoderTag = Encoding.ASCII.GetString(data, endOffset, EncoderTagLength);
        encoderDelay = delay;
        encoderPadding = padding;
    }

    /// <summary>
    /// Sees if a frame contains a Xing header
    /// </summary>
    private XingHeader()
    {
    }

    /// <summary>
    /// Number of frames
    /// </summary>
    public int Frames
    {
        get 
        { 
            if(framesOffset == -1) 
                return -1;
            return ReadBigEndian(frame.RawData, framesOffset); 
        }
        set
        {
            if (framesOffset == -1)
                throw new InvalidOperationException("Frames flag is not set");
            WriteBigEndian(frame.RawData, framesOffset, value);
        }
    }

    /// <summary>
    /// Number of bytes
    /// </summary>
    public int Bytes
    {
        get 
        { 
            if(bytesOffset == -1) 
                return -1;
            return ReadBigEndian(frame.RawData, bytesOffset); 
        }
        set
        {
            if (framesOffset == -1)
                throw new InvalidOperationException("Bytes flag is not set");
            WriteBigEndian(frame.RawData, bytesOffset, value);
        }
    }

    /// <summary>
    /// VBR Scale property
    /// </summary>
    public int VbrScale
    {
        get { return vbrScale; }
    }

    /// <summary>
    /// The MP3 frame
    /// </summary>
    public Mp3Frame Mp3Frame
    {
        get { return frame; }
    }

    /// <summary>
    /// The nine-character encoder signature from the LAME-style extension that follows the
    /// Xing/Info fields - "LAME3.100", "Lavc60.31" and so on - or <c>null</c> when this header
    /// carries no such extension.
    /// </summary>
    public string EncoderTag
    {
        get { return encoderTag; }
    }

    /// <summary>
    /// True when the encoder extension was found and <see cref="EncoderDelay"/> and
    /// <see cref="EncoderPadding"/> came from it rather than being unknown.
    /// </summary>
    public bool HasEncoderDelayInfo
    {
        get { return encoderTag != null; }
    }

    /// <summary>
    /// How many samples of silence the encoder put in front of the audio, per channel. These
    /// are priming samples that were never in the original signal; a gapless decoder discards
    /// them, along with the decoder's own 529-sample delay. Zero when the header carries no
    /// encoder extension.
    /// </summary>
    public int EncoderDelay
    {
        get { return encoderDelay; }
    }

    /// <summary>
    /// How many samples of silence the encoder added after the audio to fill the last frame,
    /// per channel. A gapless decoder drops them, so that the decoded length matches the
    /// original. Zero when the header carries no encoder extension.
    /// </summary>
    public int EncoderPadding
    {
        get { return encoderPadding; }
    }

}
