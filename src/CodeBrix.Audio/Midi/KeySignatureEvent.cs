using System;
using System.IO;

namespace CodeBrix.Audio.Midi; //was previously: NAudio.Midi;

/// <summary>
/// Represents a MIDI key signature event event
/// </summary>
/// <remarks>
/// The specification allows a sharps/flats count of -7 to 7 and a major/minor flag of 0 or 1, but
/// generators exist that write neither - some stem exporters put an opaque number in the
/// sharps/flats byte. Read with <see cref="MidiReadMode.Tolerant"/>, this type keeps whatever bytes
/// were in the file (see <see cref="RawSharpsFlats"/> and <see cref="RawMajorMinor"/>), reports
/// <see cref="IsWithinSpecification"/> as <see langword="false"/>, and writes the same bytes back on
/// export. Read with <see cref="MidiReadMode.Strict"/>, an out-of-range value throws.
/// </remarks>
public class KeySignatureEvent : MetaEvent
{
    private readonly byte sharpsFlats;
    private readonly byte majorMinor;
    private readonly bool withinSpecification;

    private static readonly string[] MajorKeyNames =
    {
        "Cb", "Gb", "Db", "Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#"
    };

    private static readonly string[] MinorKeyNames =
    {
        "Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#", "G#", "D#", "A#"
    };

    /// <summary>
    /// Reads a new track sequence number event from a MIDI stream, rejecting anything the
    /// specification does not allow.
    /// </summary>
    /// <param name="br">The MIDI stream</param>
    /// <param name="length">the data length</param>
    /// <exception cref="FormatException">The length is not 2, or the sharps/flats or major/minor
    /// value is outside the range the specification allows.</exception>
    public KeySignatureEvent(BinaryReader br, int length)
        : this(br, length, MidiReadMode.Strict)
    {
    }

    /// <summary>
    /// Reads a new key signature event from a MIDI stream.
    /// </summary>
    /// <param name="br">The MIDI stream</param>
    /// <param name="length">the data length</param>
    /// <param name="readMode">
    /// <see cref="MidiReadMode.Strict"/> to throw when the sharps/flats or major/minor value is
    /// outside the range the specification allows; <see cref="MidiReadMode.Tolerant"/> to keep the
    /// bytes as they were and report <see cref="IsWithinSpecification"/> as <see langword="false"/>.
    /// </param>
    /// <exception cref="FormatException">The length is not 2 (in either mode), or a value is out of
    /// range and <paramref name="readMode"/> is <see cref="MidiReadMode.Strict"/>.</exception>
    public KeySignatureEvent(BinaryReader br, int length, MidiReadMode readMode)
    {
        if (length != 2)
        {
            throw new FormatException("Invalid key signature length");
        }

        var sharpsFlatsByte = br.ReadByte(); // sf=sharps/flats (-7=7 flats, 0=key of C,7=7 sharps)
        var majorMinorByte = br.ReadByte(); // mi=major/minor (0=major, 1=minor)

        withinSpecification = IsInSpecifiedRange(sharpsFlatsByte, majorMinorByte);

        if (!withinSpecification && readMode == MidiReadMode.Strict)
        {
            if ((sbyte)sharpsFlatsByte < -7 || (sbyte)sharpsFlatsByte > 7)
            {
                throw new FormatException($"Invalid key signature sharps/flats value {(sbyte)sharpsFlatsByte}. Expected range is -7 to 7.");
            }

            throw new FormatException($"Invalid key signature major/minor value {majorMinorByte}. Expected 0 (major) or 1 (minor).");
        }

        sharpsFlats = sharpsFlatsByte;
        majorMinor = majorMinorByte;
    }

    /// <summary>
    /// Creates a new Key signature event with the specified data
    /// </summary>
    /// <param name="sharpsFlats">Number of sharps (positive) or flats (negative), -7 to 7.</param>
    /// <param name="majorMinor">0 for a major key, 1 for a minor key.</param>
    /// <param name="absoluteTime">Absolute time of this event.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside the range the specification
    /// allows. Use <see cref="FromRawValues"/> to build an event that deliberately carries
    /// out-of-range bytes.</exception>
    public KeySignatureEvent(int sharpsFlats, int majorMinor, long absoluteTime)
        : base(MetaEventType.KeySignature, 2, absoluteTime)
    {
        if (sharpsFlats < -7 || sharpsFlats > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(sharpsFlats), sharpsFlats,
                "Sharps/flats value must be in the range -7 to 7.");
        }

        if (majorMinor < 0 || majorMinor > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(majorMinor), majorMinor,
                "Major/minor value must be 0 (major) or 1 (minor).");
        }

        this.sharpsFlats = (byte)sharpsFlats;
        this.majorMinor = (byte)majorMinor;
        withinSpecification = true;
    }

    private KeySignatureEvent(byte sharpsFlats, byte majorMinor, long absoluteTime, bool withinSpecification)
        : base(MetaEventType.KeySignature, 2, absoluteTime)
    {
        this.sharpsFlats = sharpsFlats;
        this.majorMinor = majorMinor;
        this.withinSpecification = withinSpecification;
    }

    /// <summary>
    /// Creates a key signature event from the two bytes as they would appear in a file, without
    /// checking them against the specification.
    /// </summary>
    /// <param name="rawSharpsFlats">The sharps/flats byte, -128 to 255. Values outside -7 to 7 are
    /// kept as they are and make <see cref="IsWithinSpecification"/> <see langword="false"/>.</param>
    /// <param name="rawMajorMinor">The major/minor byte, 0 to 255. Values above 1 are kept as they
    /// are and make <see cref="IsWithinSpecification"/> <see langword="false"/>.</param>
    /// <param name="absoluteTime">Absolute time of this event.</param>
    /// <returns>An event that exports exactly those two bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value does not fit in one byte.</exception>
    public static KeySignatureEvent FromRawValues(int rawSharpsFlats, int rawMajorMinor, long absoluteTime)
    {
        if (rawSharpsFlats < sbyte.MinValue || rawSharpsFlats > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(rawSharpsFlats), rawSharpsFlats,
                "The sharps/flats value must fit in a single byte.");
        }

        if (rawMajorMinor < 0 || rawMajorMinor > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(rawMajorMinor), rawMajorMinor,
                "The major/minor value must fit in a single byte.");
        }

        var sharpsFlatsByte = unchecked((byte)rawSharpsFlats);
        var majorMinorByte = (byte)rawMajorMinor;
        return new KeySignatureEvent(sharpsFlatsByte, majorMinorByte, absoluteTime,
            IsInSpecifiedRange(sharpsFlatsByte, majorMinorByte));
    }

    private static bool IsInSpecifiedRange(byte sharpsFlatsByte, byte majorMinorByte) =>
        (sbyte)sharpsFlatsByte >= -7 && (sbyte)sharpsFlatsByte <= 7 && majorMinorByte <= 1;

    /// <summary>
    /// Creates a deep clone of this MIDI event.
    /// </summary>
    public override MidiEvent Clone() => (KeySignatureEvent)MemberwiseClone();

    /// <summary>
    /// Number of sharps or flats, the sharps/flats byte read as a signed value. Only meaningful when
    /// <see cref="IsWithinSpecification"/> is <see langword="true"/>.
    /// </summary>
    public int SharpsFlats => (sbyte)sharpsFlats;

    /// <summary>
    /// Major or Minor key. Only meaningful when <see cref="IsWithinSpecification"/> is
    /// <see langword="true"/>.
    /// </summary>
    public int MajorMinor => majorMinor;

    /// <summary>
    /// The sharps/flats byte exactly as it appeared in the file, 0 to 255.
    /// </summary>
    public int RawSharpsFlats => sharpsFlats;

    /// <summary>
    /// The major/minor byte exactly as it appeared in the file, 0 to 255.
    /// </summary>
    public int RawMajorMinor => majorMinor;

    /// <summary>
    /// Whether both values are inside the range the Standard MIDI File specification allows. When
    /// this is <see langword="false"/> the event is not a key signature in any musical sense - only
    /// <see cref="RawSharpsFlats"/> and <see cref="RawMajorMinor"/> mean anything.
    /// </summary>
    public bool IsWithinSpecification => withinSpecification;

    /// <summary>
    /// The musical key name represented by this event (for example Bb major, C# minor), or a
    /// description of the raw bytes when <see cref="IsWithinSpecification"/> is
    /// <see langword="false"/>.
    /// </summary>
    public string KeyName
    {
        get
        {
            if (!withinSpecification)
            {
                return $"Unknown key (sf {RawSharpsFlats}, mi {RawMajorMinor})";
            }

            var index = SharpsFlats + 7;
            var tonic = MajorMinor == 0 ? MajorKeyNames[index] : MinorKeyNames[index];
            return MajorMinor == 0 ? $"{tonic} major" : $"{tonic} minor";
        }
    }

    /// <summary>
    /// Describes this event
    /// </summary>
    /// <returns>String describing the event</returns>
    public override string ToString()
    {
        return $"{base.ToString()} {KeyName}";
    }

    /// <summary>
    /// Calls base class export first, then exports the data
    /// specific to this event
    /// <seealso cref="MidiEvent.Export">MidiEvent.Export</seealso>
    /// </summary>
    public override void Export(ref long absoluteTime, BinaryWriter writer)
    {
        base.Export(ref absoluteTime, writer);
        writer.Write(sharpsFlats);
        writer.Write(majorMinor);
    }
}
