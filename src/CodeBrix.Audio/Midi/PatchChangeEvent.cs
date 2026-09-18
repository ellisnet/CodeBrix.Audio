using System;
using System.IO;
using System.Text;

namespace CodeBrix.Audio.Midi; //was previously: NAudio.Midi;

/// <summary>
/// Represents a MIDI patch change event
/// </summary>
public class PatchChangeEvent : MidiEvent
{
    private byte patch;

    /// <summary>
    /// Gets the official General MIDI Level 1 name of a program number.
    /// </summary>
    /// <param name="patchNumber">The program number, 0 to 127.</param>
    /// <returns>The name the MIDI Association publishes, for example <c>"Acoustic Grand Piano"</c>
    /// or <c>"Lead 1 (square)"</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="patchNumber"/> is outside the
    /// range 0 to 127.</exception>
    //was previously: this class carried its own 128-entry table of abbreviated names, three of them
    //misspelled upstream - "Accoridan" for Accordion, "Tango Accordian" for Tango Accordion and
    //"Skakuhachi" for Shakuhachi - so every ToString() of one of those patch changes printed the
    //typo, and the rest printed an abbreviation ("Acoustic Grand", "Clav") rather than the
    //published name. The names now come from the one table
    //in GeneralMidi, which holds the sound set exactly as the MIDI Association publishes it, so
    //there is no second list to drift from it. The old table also indexed without a check, which
    //made an out-of-range number an IndexOutOfRangeException; it is now the
    //ArgumentOutOfRangeException the argument deserves, and nothing in this package could reach
    //that path anyway, because Patch is validated on the way in.
    public static string GetPatchName(int patchNumber)
    {
        if (patchNumber < 0 || patchNumber > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(patchNumber), patchNumber,
                "Patch number must be in the range 0-127");
        }

        return GeneralMidi.DisplayName((GeneralMidiProgram)patchNumber);
    }

    /// <summary>
    /// Reads a new patch change event from a MIDI stream
    /// </summary>
    /// <param name="br">Binary reader for the MIDI stream</param>
    public PatchChangeEvent(BinaryReader br)
    {
        patch = br.ReadByte();
        if ((patch & 0x80) != 0)
        {
            // TODO: might be a follow-on
            throw new FormatException("Invalid patch");
        }
    }

    /// <summary>
    /// Creates a new patch change event
    /// </summary>
    /// <param name="absoluteTime">Time of the event</param>
    /// <param name="channel">Channel number</param>
    /// <param name="patchNumber">Patch number</param>
    public PatchChangeEvent(long absoluteTime, int channel, int patchNumber)
        : base(absoluteTime, channel, MidiCommandCode.PatchChange)
    {
        this.Patch = patchNumber;
    }

    /// <summary>
    /// The Patch Number
    /// </summary>
    public int Patch
    {
        get
        {
            return patch;
        }
        set
        {
            if (value < 0 || value > 127)
            {
                throw new ArgumentOutOfRangeException("value", "Patch number must be in the range 0-127");
            }
            patch = (byte)value;
        }
    }

    /// <summary>
    /// Describes this patch change event
    /// </summary>
    /// <returns>String describing the patch change event</returns>
    public override string ToString()
    {
        return String.Format("{0} {1}",
            base.ToString(),
            GetPatchName(this.patch));
    }

    /// <summary>
    /// Gets as a short message for sending with the midiOutShortMsg API
    /// </summary>
    /// <returns>short message</returns>
    public override int GetAsShortMessage()
    {
        return base.GetAsShortMessage() + (this.patch << 8);
    }

    /// <summary>
    /// Calls base class export first, then exports the data 
    /// specific to this event
    /// <seealso cref="MidiEvent.Export">MidiEvent.Export</seealso>
    /// </summary>
    public override void Export(ref long absoluteTime, BinaryWriter writer)
    {
        base.Export(ref absoluteTime, writer);
        writer.Write(patch);
    }
}
