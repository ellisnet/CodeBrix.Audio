using Android.App;
using Android.Content;
using Android.Media.Midi;

namespace CodeBrix.Audio.MidiConnect.AndroidTests;

[Service(Name = "com.codebrix.midiconnect.tests.EchoService", Exported = true,
    Permission = "android.permission.BIND_MIDI_DEVICE_SERVICE")]
[IntentFilter(new[] { "android.media.midi.MidiDeviceService" })]
[MetaData("android.media.midi.MidiDeviceService", Resource = "@xml/midi_device_info")]
public sealed class EchoService : MidiDeviceService
{
    private MidiReceiver receiver;
    public override MidiReceiver[] OnGetInputPortReceivers() => new[] { receiver ??= new EchoReceiver(this) };

    private sealed class EchoReceiver : MidiReceiver
    {
        private readonly EchoService service;
        internal EchoReceiver(EchoService service) => this.service = service;
        public override void OnSend(byte[] data, int offset, int count, long timestamp)
        {
            var outputs = service.GetOutputPortReceivers();
            if (outputs.Length > 0) outputs[0].Send(data, offset, count, timestamp == 0 ? Java.Lang.JavaSystem.NanoTime() : timestamp);
        }
    }
}
