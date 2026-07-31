using System;
using System.IO;

// ReSharper disable once CheckNamespace
namespace CodeBrix.Audio.Synth; //was previously: MeltySynth

internal static class Modulator
{
    // Since modulators will not be supported, we discard the data.
    internal static void DiscardData(BinaryReader reader, int size)
    {
        if (size % 10 != 0)
        {
            throw new InvalidDataException("The modulator list is invalid.");
        }

        reader.BaseStream.Position += size;
    }
}
