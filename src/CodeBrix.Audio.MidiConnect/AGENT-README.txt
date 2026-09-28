================================================================================
AGENT-README: CodeBrix.Audio.MidiConnect.MitLicenseForever
================================================================================

PURPOSE AND DEPENDENCIES
========================
MIDI 1.0 physical/registered virtual device I/O, recording, and timed device
playback. Targets net10.0 and net10.0-android (API 33+). The only NuGet dependency
is CodeBrix.Audio.Core.MitLicenseForever, produced at the same build version.
Do NOT reference the desktop audio package just to use MIDI. For synthesized or
PCM audio output the consumer independently selects CodeBrix.Audio.MitLicenseForever
or CodeBrix.Audio.Android.ApacheLicenseForever. No audio initialization is needed
for MidiConnect alone. The namespace is CodeBrix.Audio.MidiConnect.

    dotnet add package CodeBrix.Audio.MidiConnect.MitLicenseForever

BACKENDS
========
Windows x64/ARM64: built-in WinMM.
macOS x64/ARM64: built-in CoreMIDI/CoreFoundation.
Linux x64/ARM64: system libasound.so.2 and accessible ALSA sequencer /dev/snd/seq.
Linux RISC-V 64: same LP64 bindings, experimental; needs compatible .NET runtime.
Android x64/ARM64: Android 13/API 33+, android.software.midi feature required.
Use the Android TFM so NuGet selects the Android implementation. A net10.0-only
assembly executing on Android reports unsupported platform rather than loading ALSA.
Individual devices can require drivers. Bluetooth pairing/opening unregistered
Bluetooth devices is outside the API. MIDI 2.0/UMP is not supported.

No native MIDI library is bundled or built. Linux's runtime library is an
explicit external prerequisite; development headers are unnecessary. A loader failure throws MidiDeviceException
with the original exception and commands for common distributions:
  Debian 13 / LMDE 7 / Raspberry Pi OS Trixie / Ubuntu 24.04:
      sudo apt install libasound2t64
  Debian 12 / Raspberry Pi OS Bookworm:
      sudo apt install libasound2
  Fedora: sudo dnf install alsa-lib
  Arch: sudo pacman -S alsa-lib
These are instructions for the administrator; the library NEVER installs software.
A loaded library with a missing sequencer or denied access gets separate guidance.
Do not suggest installing libasound to cure a permissions or kernel-device error.

DISCOVER, OPEN, RECEIVE
======================
    using var manager = new MidiDeviceManager();
    foreach (var port in manager.GetInputs())
        Console.WriteLine($"{port.Id}: {port.Name}");
    using var input = await manager.OpenInputAsync(chosenId);
    input.MessageReceived += (_, e) => Handle(e.Message, e.Timestamp);
    input.Error += (_, e) => Report(e.Exception);
    input.Start();

There is no reception until Start. Subscribe before calling it. Repeated Start
while running is a no-op. Dispose stops and closes the input. There is no
stop/restart on one input; open a new connection after disposal or fatal error.
GetInputs/GetOutputs refresh; Refresh returns both app-facing directions.
An OS/device OUTPUT port is an application INPUT. This is particularly easy to
reverse on Android and with controller ports named "MIDI Out".

MidiPortInfo: Id, Name, Direction, Manufacturer (may be empty).
Ids are session addresses, NOT durable serial numbers. ALSA/WinMM reuse them.
Use fresh discovery and user selection after a reconnect. Names may not be unique.
Manager owns all its connections. Its Dispose closes them; disposing a connection
individually is also valid. Opening accepts cancellation, including Android's
asynchronous native opening. A late Android completion after cancellation closes
the returned native device.

Refresh/StartWatching detect additions, removals and changed descriptors.
StartWatching(interval) defaults to 500ms, minimum 100ms. It is POLLING and can
miss a disconnect/reconnect between polls. Removed inputs fault; removed outputs
close. PortsChanged runs on the refreshing thread, or a timer worker when watching.
Error reports watcher/notification failures. StopWatching stops future polling;
an in-flight callback may finish. Watchers do not initialize any audio backend.

MESSAGES, TIMESTAMPS AND THREADS
================================
MidiPacket is one complete validated MIDI 1.0 message with owned storage:
  Data (ReadOnlyMemory<byte>), Status, Command, Channel, Data1, Data2,
  IsNoteOn, IsNoteOff, IsSystemExclusive, ToMidiEvent(absoluteTicks).
Channels are 1-16, zero for system messages. Notes/programs/controllers/velocities
are 0-127. Zero-velocity note-on is IsNoteOff. Data1/Data2 are zero when absent.
SysEx includes F0/F7; embedded realtime is delivered separately.
ChannelMessage(command, channel, data1, data2) validates and constructs a packet.
ToMidiEvent maps channel messages/SysEx to Core and system common/realtime to
Standard MIDI File F7 escapes (not an ordinary FF meta event).

The parser accepts fragmentation, multiple messages per callback, running status
and realtime interleaving. Undefined status and malformed/truncated messages that
are interrupted by a new status are reported through Error. The parser resynchronizes.
An unterminated final fragment is not a complete message and is not dispatched.
Oversized SysEx is discarded with an error; subsequent messages can continue.

MessageReceived runs serially on a worker, not the UI or native callback thread.
Keep handlers quick. Synchronous subscriber exceptions go to Error, with other
subscribers continuing; unhandled async-void continuations cannot be caught there.
Error-handler exceptions are swallowed to protect callback boundaries.
A handler may keep a packet. Its bytes do not point into native receive storage.

MidiInputOptions bounds pending messages (4096), total pending bytes (4 MiB),
and one incoming SysEx (1 MiB). Dispatch overflow FAULTS and closes the input;
never silently discard a note-off and pretend capture succeeded. Native buffers
are also bounded (ALSA uses a 2000-cell kernel input pool and 64 KiB read buffer).
The managed SysEx limit is a ceiling, not a guarantee every native driver accepts
that size or every traffic rate. ALSA FIFO overrun is reported as lost input.

MidiClock.Now is a process-local monotonic TimeSpan, not UTC. Receive timestamps
are taken before dispatch, translated from the OS clock where available. Precision
is backend-dependent (WinMM timestamps have millisecond resolution). SysEx keeps
its first-fragment timestamp; a realtime packet interleaved inside it can be
emitted first with a later timestamp. Recording sorts timestamps stably.

Input Dispose drops queued dispatch and closes native resources. A currently
executing handler may finish after disposal. Completion finishes after dispatch
and cleanup; never synchronously wait for Completion from an input handler.
Always dispose native connections/managers. A driver that refuses to relinquish
native buffers can force them to remain retained rather than permit a native
use-after-free; cleanup errors are surfaced.

RECORD
======
    using var recorder = new MidiRecorder(input);
    input.Start();
    // ... receive the performance ...
    MidiRecording take = recorder.Stop();
    take.Save("take.mid");

Recorder borrows input, neither starts nor closes it. It captures events dispatched
while attached whose arrival timestamps are at/after its start. Stop/Dispose detach;
Stop remains callable after Dispose. A callback still queued when Stop acquires the
recording lock is outside the snapshot. Await application-defined message boundaries
when exact start/stop synchronization is needed.
Defaults: 1,000,000 messages, 64 MiB message bytes. Capacity/fatal input failure
stops capture; take.Failure holds the reason and the partial capture remains usable.
Check input.Error as well for recoverable malformed-message/oversize errors.

MidiRecording.Messages: MidiTimedMessage(Time, Message), relative to start.
Duration includes trailing silence. Notes expose Channel, Note, Velocity,
ReleaseVelocity, Start, Duration, IsTruncated. Pair overlapping identical keys FIFO.
Duration measures key/pad hold time, not sustain-pedal sound. CC120 and CC123-127
or system reset release active note analysis; actual controllers remain recorded.
IsTruncated means the key was held when recording ended.

ToMidiEventCollection(beatsPerMinute=120, ticksPerQuarterNote=960,
releaseAtEnd=true) creates a type-0 collection at a constant tempo with elapsed
performance timing preserved to the chosen tick resolution. Appends held-note offs
and sustain release at the end by default. Original Messages do not change.
System-common/realtime packets use SMF F7 escapes. Save exports this collection;
for verbatim ending behavior pass releaseAtEnd:false and call Core MidiFile.Export.
Notes without note-offs may be repaired by Core's tolerant reader later.

OUTPUT
======
    using var output = await manager.OpenOutputAsync(chosenId);
    output.NoteOn(1, 60, 100);
    output.NoteOff(1, 60);
    output.ControlChange(1, 64, 0);
    output.ProgramChange(1, 0);
    output.PitchBend(1, 8192);
    output.Send(new byte[] { 0xF0, 0x7D, 1, 0xF7 });

Send accepts one complete packet, validates it, and serializes concurrent sends
and disposal. Native sends can block, especially SysEx on WinMM. Driver errors
throw. Panic(channel=0) releases sustain and sends CC123/CC120 on the chosen
channel or all channels. Direct output disposal closes without implicit panic.
A controller with no output destination cannot synthesize this music; choose
an actual instrument/output endpoint. MIDI is instructions, not PCM audio.

TIMED PLAYBACK
==============
    var file = new CodeBrix.Audio.Midi.MidiFile("song.mid");
    // Check file.Problems if using the default tolerant read mode.
    var sequence = MidiPlaybackSequence.FromMidiFile(file);
    await using var player = new MidiDevicePlayer(output);
    await player.PlayAsync(sequence, cancellationToken);

MidiPlaybackSequence.FromEvents accepts Core MidiEventCollection. Do not mutate
it during conversion. Type 0/1, PPQN tempo changes and SMPTE (including 29.97 drop
frame) are supported. Equal-time messages keep track/event order. Tempos in any
track are applied in that order. Metadata is not sent to a device. Type 2 has
separate songs: select/convert one first. Complete SysEx and F7-escaped complete
messages are supported; split/timed SysEx and incomplete escape messages are
explicitly rejected BEFORE playback. Core preserves such file events for editing.
For your own timeline construct MidiPlaybackSequence from MidiTimedMessage items
and optionally a duration. To replay a raw recording use its Messages/Duration.

One operation per player; concurrent PlayAsync throws. StopAsync cancels and waits
for output cleanup. Scheduling is best effort, monotonic, with no accumulated
relative-delay drift; not hard realtime or audio-sample-accurate. Native send latency
can delay later messages/cancellation. The playback task reports send errors and
cancellation. Ending/failing/cancelling sends sustain-off, all-notes-off and
all-sound-off on USED channels. Reserve those channels for this performance.
Player borrows output. Dispose requests cancellation; DisposeAsync waits for cleanup.
Await cleanup before closing output. There is no seek/pause/loop API.

MPE AND LATCH
=============
MidiConnect transports MIDI 1.0 MPE: RPN zone/pitch-range configuration, per-member
pitch bend, channel pressure, CC74, and attack/release velocities all survive
capture, file export and playback. For an MPE controller such as the ROLI
Seaboard RISE 2, the synthesizer must interpret these expressions. Configure
Core's MPE synthesis mode/zones/bend range to match the controller. Do not flatten
member channels. MidiMusicPlayer.SendMidiMessage takes 0-15 channels, so forward
channel messages with packet.Channel - 1, packet.Command, packet.Data1, packet.Data2.
Do not call a raw IMidiSynthesizer concurrently with rendering.

Pad latch state belongs to the app: note-on toggles a sustained voice or repeating
pattern, and physical note-off does not release that app-owned voice. Keep raw
capture unchanged. There is no universal MIDI latch/LED command. Hardware lights
need a device-specific feedback path. Give pads unique note mappings when they
need independent toggles. MPE latch additionally
requires expression-state handling when a released note's channel gets reused.

PROVENANCE
==========
Provenance and full license notices are in the package's THIRD-PARTY-NOTICES.txt.
OS MIDI services and Linux's libasound are external prerequisites, not bundled
native assets.
================================================================================
