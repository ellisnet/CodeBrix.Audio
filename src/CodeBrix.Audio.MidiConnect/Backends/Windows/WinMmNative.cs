using System;
using System.Runtime.InteropServices;

namespace CodeBrix.Audio.MidiConnect.Backends.Windows; //was previously: NAudio.Midi

// Adapted from NAudio.WinMM MIDI interop; narrowed to device I/O and corrected to the
// pointer-sized Win64 ABI. See THIRD-PARTY-NOTICES.txt. Microsoft MIDIHDR documentation:
// https://learn.microsoft.com/windows/win32/api/mmeapi/ns-mmeapi-midihdr
internal static unsafe class WinMmNative
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void Callback(IntPtr handle, uint message, UIntPtr instance, UIntPtr parameter1, UIntPtr parameter2);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct InputCaps
    {
        internal ushort Manufacturer, Product;
        internal uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Name;
        internal uint Support;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct OutputCaps
    {
        internal ushort Manufacturer, Product;
        internal uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Name;
        internal ushort Technology, Voices, Notes, ChannelMask;
        internal uint Support;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Header
    {
        internal IntPtr Data;
        internal uint Length, Recorded;
        internal UIntPtr User;
        internal uint Flags;
        internal IntPtr Next;
        internal UIntPtr Reserved;
        internal uint Offset;
        internal fixed ulong ReservedArray[8]; // Only 64-bit Windows is supported.
    }
    [DllImport("winmm.dll")] internal static extern uint midiInGetNumDevs();
    [DllImport("winmm.dll")] internal static extern uint midiOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] internal static extern uint midiInGetDevCapsW(UIntPtr id, out InputCaps caps, uint size);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] internal static extern uint midiOutGetDevCapsW(UIntPtr id, out OutputCaps caps, uint size);
    [DllImport("winmm.dll")] internal static extern uint midiInOpen(out IntPtr handle, uint id, Callback callback, UIntPtr instance, uint flags);
    [DllImport("winmm.dll")] internal static extern uint midiInStart(IntPtr handle);
    [DllImport("winmm.dll")] internal static extern uint midiInStop(IntPtr handle);
    [DllImport("winmm.dll")] internal static extern uint midiInReset(IntPtr handle);
    [DllImport("winmm.dll")] internal static extern uint midiInClose(IntPtr handle);
    [DllImport("winmm.dll")] internal static extern uint midiInPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] internal static extern uint midiInUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] internal static extern uint midiInAddBuffer(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] internal static extern uint midiOutOpen(out IntPtr handle, uint id, IntPtr callbackEvent, UIntPtr instance, uint flags);
    [DllImport("winmm.dll")] internal static extern uint midiOutShortMsg(IntPtr handle, uint message);
    [DllImport("winmm.dll")] internal static extern uint midiOutPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] internal static extern uint midiOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] internal static extern uint midiOutLongMsg(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] internal static extern uint midiOutReset(IntPtr handle);
    [DllImport("winmm.dll")] internal static extern uint midiOutClose(IntPtr handle);
    internal static readonly uint HeaderSize = (uint)Marshal.SizeOf<Header>();
    internal static void Check(uint result, string operation)
    {
        if (result != 0) throw new MidiDeviceException($"Windows MIDI {operation} failed with MMRESULT {result}.");
    }

    internal sealed class Buffer : IDisposable
    {
        internal IntPtr Pointer { get; private set; }
        internal Header Value => Marshal.PtrToStructure<Header>(Pointer);
        internal Buffer(int capacity)
        {
            var data = Marshal.AllocHGlobal(capacity);
            try
            {
                Pointer = Marshal.AllocHGlobal((int)HeaderSize);
                Marshal.StructureToPtr(new Header { Data = data, Length = (uint)capacity }, Pointer, false);
            }
            catch { Marshal.FreeHGlobal(data); if (Pointer != IntPtr.Zero) Marshal.FreeHGlobal(Pointer); throw; }
        }
        public void Dispose()
        {
            if (Pointer == IntPtr.Zero) return;
            Marshal.FreeHGlobal(Value.Data);
            Marshal.FreeHGlobal(Pointer);
            Pointer = IntPtr.Zero;
        }
    }
}
