using System;
using System.Runtime.InteropServices;

namespace CodeBrix.Audio.MidiConnect.Backends.MacOS;

// Apple's documented CoreMIDI/CoreFoundation ABI. MIDIObjectRef is UInt32 even on 64-bit macOS.
// https://developer.apple.com/documentation/coremidi
internal static unsafe class CoreMidiNative
{
    private const string MidiLibrary = "/System/Library/Frameworks/CoreMIDI.framework/CoreMIDI";
    private const string CfLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void ReadCallback(IntPtr packets, IntPtr context, IntPtr sourceContext);
    [StructLayout(LayoutKind.Sequential)] internal struct Timebase { internal uint Numerator, Denominator; }
    [DllImport(MidiLibrary)] internal static extern nuint MIDIGetNumberOfSources();
    [DllImport(MidiLibrary)] internal static extern nuint MIDIGetNumberOfDestinations();
    [DllImport(MidiLibrary)] internal static extern uint MIDIGetSource(nuint index);
    [DllImport(MidiLibrary)] internal static extern uint MIDIGetDestination(nuint index);
    [DllImport(MidiLibrary)] internal static extern int MIDIObjectGetStringProperty(uint obj, IntPtr property, out IntPtr value);
    [DllImport(MidiLibrary)] internal static extern int MIDIObjectGetIntegerProperty(uint obj, IntPtr property, out int value);
    [DllImport(MidiLibrary)] internal static extern int MIDIClientCreate(IntPtr name, IntPtr notify, IntPtr context, out uint client);
    [DllImport(MidiLibrary)] internal static extern int MIDIClientDispose(uint client);
    [DllImport(MidiLibrary)] internal static extern int MIDIInputPortCreate(uint client, IntPtr name, ReadCallback callback, IntPtr context, out uint port);
    [DllImport(MidiLibrary)] internal static extern int MIDIOutputPortCreate(uint client, IntPtr name, out uint port);
    [DllImport(MidiLibrary)] internal static extern int MIDIPortDispose(uint port);
    [DllImport(MidiLibrary)] internal static extern int MIDIPortConnectSource(uint port, uint source, IntPtr context);
    [DllImport(MidiLibrary)] internal static extern int MIDIPortDisconnectSource(uint port, uint source);
    [DllImport(MidiLibrary)] internal static extern int MIDISend(uint port, uint destination, IntPtr packets);
    [DllImport(MidiLibrary)] internal static extern IntPtr MIDIPacketListInit(IntPtr packets);
    [DllImport(MidiLibrary)] internal static extern IntPtr MIDIPacketListAdd(IntPtr packets, nuint size, IntPtr currentPacket, ulong time, nuint length, byte* bytes);
    [DllImport(CfLibrary)] internal static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);
    [DllImport(CfLibrary)] internal static extern nint CFStringGetLength(IntPtr text);
    [DllImport(CfLibrary)] internal static extern nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);
    [DllImport(CfLibrary)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool CFStringGetCString(IntPtr text, byte[] buffer, nint capacity, uint encoding);
    [DllImport(CfLibrary)] internal static extern void CFRelease(IntPtr obj);
    [DllImport("/usr/lib/libSystem.B.dylib")] internal static extern ulong mach_absolute_time();
    [DllImport("/usr/lib/libSystem.B.dylib")] internal static extern int mach_timebase_info(out Timebase info);

    internal static void Check(int status, string operation)
    {
        if (status != 0) throw new MidiDeviceException($"CoreMIDI {operation} failed with OSStatus {status}.");
    }
    internal static IntPtr Text(string value)
    {
        IntPtr result = CFStringCreateWithCString(IntPtr.Zero, value, 0x08000100);
        return result != IntPtr.Zero ? result : throw new OutOfMemoryException("Could not allocate a CoreFoundation string.");
    }
    internal static string GetString(uint obj, string key)
    {
        IntPtr property = Text(key);
        try
        {
            if (MIDIObjectGetStringProperty(obj, property, out var value) != 0 || value == IntPtr.Zero) return "";
            try
            {
                nint size = CFStringGetMaximumSizeForEncoding(CFStringGetLength(value), 0x08000100) + 1;
                var bytes = new byte[checked((int)size)];
                if (!CFStringGetCString(value, bytes, size, 0x08000100)) return "";
                int length = Array.IndexOf(bytes, (byte)0);
                return System.Text.Encoding.UTF8.GetString(bytes, 0, length < 0 ? bytes.Length : length);
            }
            finally { CFRelease(value); }
        }
        finally { CFRelease(property); }
    }
    internal static int UniqueId(uint endpoint)
    {
        IntPtr key = Text("uniqueID");
        try { Check(MIDIObjectGetIntegerProperty(endpoint, key, out var value), "read endpoint identity"); return value; }
        finally { CFRelease(key); }
    }

    // Derive native packet alignment from CoreMIDI's own builder. PacketNext has historically
    // been an SDK inline helper, so importing it as a dylib symbol is not portable.
    internal static readonly (int FirstOffset, bool AlignFour) PacketLayout = GetPacketLayout();
    private static (int, bool) GetPacketLayout()
    {
        IntPtr list = Marshal.AllocHGlobal(1024);
        try
        {
            byte value = 0xF8;
            var first = MIDIPacketListAdd(list, 1024, MIDIPacketListInit(list), 1, 1, &value);
            if (first == IntPtr.Zero) throw new PlatformNotSupportedException("CoreMIDI could not initialize a packet list.");
            var second = MIDIPacketListAdd(list, 1024, first, 2, 1, &value);
            if (second == IntPtr.Zero) throw new PlatformNotSupportedException("CoreMIDI could not append a packet.");
            int offset = checked((int)(first.ToInt64() - list.ToInt64()));
            long stride = second.ToInt64() - first.ToInt64();
            if (first == IntPtr.Zero || second == IntPtr.Zero || (stride != 11 && stride != 12) || offset < 4 || offset > 16)
                throw new PlatformNotSupportedException("Unrecognized CoreMIDI packet layout.");
            return (offset, stride == 12);
        }
        finally { Marshal.FreeHGlobal(list); }
    }
}
