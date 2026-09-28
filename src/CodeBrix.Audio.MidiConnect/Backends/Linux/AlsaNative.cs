using System;
using System.Runtime.InteropServices;

namespace CodeBrix.Audio.MidiConnect.Backends.Linux;

// Independently authored declarations of ALSA's documented C ABI. No alsa-sharp source.
// https://www.alsa-project.org/alsa-doc/alsa-lib/group___sequencer.html
// https://www.alsa-project.org/alsa-doc/alsa-lib/group___m_i_d_i___event.html
// All supported Linux architectures are LP64. C long/size_t use nint/nuint.
internal static unsafe class AlsaNative
{
    private const string Library = "libasound.so.2";
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Address { internal byte Client; internal byte Port; }
    [StructLayout(LayoutKind.Explicit, Size = 28, Pack = 1)]
    internal struct Event
    {
        [FieldOffset(0)] internal byte Type;
        [FieldOffset(1)] internal byte Flags;
        [FieldOffset(3)] internal byte Queue;
        [FieldOffset(4)] internal uint Seconds;
        [FieldOffset(8)] internal uint Nanoseconds;
        [FieldOffset(12)] internal Address Source;
        [FieldOffset(14)] internal Address Destination;
        [FieldOffset(16)] internal uint ExternalLength;
        [FieldOffset(20)] internal IntPtr ExternalData;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct PollFd { internal int Fd; internal short Events; internal short ReturnedEvents; }

    [DllImport(Library)] internal static extern int snd_seq_open(out IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int streams, int mode);
    [DllImport(Library)] internal static extern int snd_seq_close(IntPtr handle);
    [DllImport(Library)] internal static extern int snd_seq_client_id(IntPtr handle);
    [DllImport(Library)] internal static extern int snd_seq_set_client_name(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Library)] internal static extern int snd_seq_set_client_pool_input(IntPtr handle, nuint cells);
    [DllImport(Library)] internal static extern int snd_seq_set_input_buffer_size(IntPtr handle, nuint bytes);
    [DllImport(Library)] internal static extern int snd_seq_client_info_malloc(out IntPtr info);
    [DllImport(Library)] internal static extern void snd_seq_client_info_free(IntPtr info);
    [DllImport(Library)] internal static extern void snd_seq_client_info_set_client(IntPtr info, int client);
    [DllImport(Library)] internal static extern int snd_seq_client_info_get_client(IntPtr info);
    [DllImport(Library)] internal static extern IntPtr snd_seq_client_info_get_name(IntPtr info);
    [DllImport(Library)] internal static extern int snd_seq_query_next_client(IntPtr handle, IntPtr info);
    [DllImport(Library)] internal static extern int snd_seq_port_info_malloc(out IntPtr info);
    [DllImport(Library)] internal static extern void snd_seq_port_info_free(IntPtr info);
    [DllImport(Library)] internal static extern void snd_seq_port_info_set_client(IntPtr info, int client);
    [DllImport(Library)] internal static extern void snd_seq_port_info_set_port(IntPtr info, int port);
    [DllImport(Library)] internal static extern int snd_seq_port_info_get_port(IntPtr info);
    [DllImport(Library)] internal static extern IntPtr snd_seq_port_info_get_name(IntPtr info);
    [DllImport(Library)] internal static extern uint snd_seq_port_info_get_capability(IntPtr info);
    [DllImport(Library)] internal static extern uint snd_seq_port_info_get_type(IntPtr info);
    [DllImport(Library)] internal static extern int snd_seq_query_next_port(IntPtr handle, IntPtr info);
    [DllImport(Library)] internal static extern int snd_seq_create_simple_port(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, uint capabilities, uint type);
    [DllImport(Library)] internal static extern int snd_seq_connect_to(IntPtr handle, int localPort, int client, int port);
    [DllImport(Library)] internal static extern int snd_seq_port_subscribe_malloc(out IntPtr subscription);
    [DllImport(Library)] internal static extern void snd_seq_port_subscribe_free(IntPtr subscription);
    [DllImport(Library)] internal static extern void snd_seq_port_subscribe_set_sender(IntPtr subscription, ref Address source);
    [DllImport(Library)] internal static extern void snd_seq_port_subscribe_set_dest(IntPtr subscription, ref Address destination);
    [DllImport(Library)] internal static extern void snd_seq_port_subscribe_set_queue(IntPtr subscription, int queue);
    [DllImport(Library)] internal static extern void snd_seq_port_subscribe_set_time_update(IntPtr subscription, int enabled);
    [DllImport(Library)] internal static extern void snd_seq_port_subscribe_set_time_real(IntPtr subscription, int enabled);
    [DllImport(Library)] internal static extern int snd_seq_subscribe_port(IntPtr handle, IntPtr subscription);
    [DllImport(Library)] internal static extern int snd_seq_alloc_named_queue(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Library)] internal static extern int snd_seq_control_queue(IntPtr handle, int queue, int type, int value, IntPtr optionalEvent);
    [DllImport(Library)] internal static extern int snd_seq_drain_output(IntPtr handle);
    [DllImport(Library)] internal static extern int snd_seq_event_input(IntPtr handle, out IntPtr ev);
    [DllImport(Library)] internal static extern int snd_seq_event_output_direct(IntPtr handle, ref Event ev);
    [DllImport(Library)] internal static extern int snd_seq_poll_descriptors_count(IntPtr handle, short events);
    [DllImport(Library)] internal static extern int snd_seq_poll_descriptors(IntPtr handle, [Out] PollFd[] descriptors, uint count, short events);
    [DllImport("libc", SetLastError = true)] internal static extern int poll([In, Out] PollFd[] descriptors, nuint count, int milliseconds);
    [DllImport(Library)] internal static extern int snd_midi_event_new(nuint size, out IntPtr coder);
    [DllImport(Library)] internal static extern void snd_midi_event_free(IntPtr coder);
    [DllImport(Library)] internal static extern void snd_midi_event_reset_encode(IntPtr coder);
    [DllImport(Library)] internal static extern void snd_midi_event_no_status(IntPtr coder, int enabled);
    [DllImport(Library)] internal static extern nint snd_midi_event_encode(IntPtr coder, byte* data, nint count, ref Event ev);
    [DllImport(Library)] internal static extern nint snd_midi_event_decode(IntPtr coder, byte* data, nint capacity, IntPtr ev);
    [DllImport(Library)] internal static extern IntPtr snd_strerror(int error);

    internal static int Check(int result, string operation)
    {
        if (result < 0) throw new MidiDeviceException($"ALSA {operation}: {Marshal.PtrToStringUTF8(snd_strerror(result))} ({result}). {FailureGuidance(result, operation)}");
        return result;
    }

    internal static string FailureGuidance(int error, string operation) => error switch
    {
        -28 => "The ALSA input FIFO overflowed and messages were lost. Capture has stopped; reduce incoming traffic or scheduling delays.",
        -13 or -1 => "Access was denied. Check this user's permissions for /dev/snd/seq and any sandbox/container device-access restrictions.",
        -2 or -19 when operation == "open" => "libasound loaded, but the ALSA sequencer is unavailable. Check that the kernel provides snd_seq and /dev/snd/seq, and that the device is exposed to this application or container.",
        _ => "Check that the MIDI device is still connected and available to this application."
    };

    internal static MidiDeviceException MissingLibrary(Exception cause) => new(
        "Linux MIDI requires the system ALSA runtime, libasound.so.2, which could not be loaded. " +
        "Install the package for your distribution and application architecture: " +
        "Debian 13 / LMDE 7 / Raspberry Pi OS Trixie / Ubuntu 24.04: sudo apt install libasound2t64; " +
        "Debian 12 / Raspberry Pi OS Bookworm: sudo apt install libasound2; " +
        "Fedora: sudo dnf install alsa-lib; Arch Linux: sudo pacman -S alsa-lib. " +
        "For other distributions, install the package providing libasound.so.2. " +
        "Development headers are not required. If already installed, check its dependencies, architecture and library search path. " +
        "MidiConnect does not install software automatically.", cause);

    internal static IntPtr Open(string name)
    {
        if (IntPtr.Size != 8) throw new PlatformNotSupportedException("The ALSA backend supports 64-bit Linux processes.");
        IntPtr handle;
        try { Check(snd_seq_open(out handle, "default", 3, 1), "open"); }
        catch (DllNotFoundException exception) { throw MissingLibrary(exception); }
        catch (BadImageFormatException exception) { throw MissingLibrary(exception); }
        try { Check(snd_seq_set_client_name(handle, name), "name client"); return handle; }
        catch { snd_seq_close(handle); throw; }
    }
}
