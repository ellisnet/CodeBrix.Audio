using CodeBrix.Audio.Engine.Midi.Structs;
using CodeBrix.Audio.Engine.Structs;

namespace CodeBrix.Audio.Engine.Midi.Interfaces;  //was previously: SoundFlow.Midi.Interfaces

/// <summary>
/// Represents a destination for MIDI messages within the MIDI routing graph.
/// </summary>
public interface IMidiDestinationNode : IMidiControllable
{
    /// <summary>
    /// Gets a user-friendly name for the destination node.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Processes an incoming MIDI message.
    /// </summary>
    /// <param name="message">The MIDI message to process.</param>
    Result ProcessMessage(MidiMessage message);
}