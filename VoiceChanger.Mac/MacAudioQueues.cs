using System.Runtime.InteropServices;

namespace VoiceChanger.Mac;

/// <summary>
/// Records from a device in fixed blocks of float samples, using Apple's AudioQueue. It converts
/// from the device's own format and rate, and calls back on its own thread rather than Core
/// Audio's real-time one, which suits managed code. A null device follows the system default mic.
/// </summary>
public sealed unsafe class MacInputQueue : MacAudioQueue
{
    private readonly Action<ReadOnlySpan<float>> _onData;
    private readonly InputCallback _callback; // kept alive for as long as the native queue can call it

    public MacInputQueue(string? deviceUid, int rate, int channels, int blockFrames, Action<ReadOnlySpan<float>> onData, Action<Exception>? failed = null)
        : base(failed)
    {
        _onData = onData;
        _callback = OnBuffer;
        var format = FloatFormat(rate, channels);
        Check(AudioQueueNewInput(ref format, _callback, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out Queue), "open the microphone");
        Prepare(deviceUid, blockFrames * channels * sizeof(float), buffer => Check(AudioQueueEnqueueBuffer(Queue, buffer, 0, IntPtr.Zero), "queue audio"));
    }

    private void OnBuffer(IntPtr user, IntPtr queue, QueueBuffer* buffer, IntPtr start, uint packets, IntPtr descriptions)
    {
        if (!Running) return;
        try
        {
            _onData(new ReadOnlySpan<float>(buffer->Data, (int)(buffer->ByteSize / sizeof(float))));
            AudioQueueEnqueueBuffer(queue, (IntPtr)buffer, 0, IntPtr.Zero);
        }
        catch (Exception ex) { Fail(ex); }
    }
}

/// <summary>Plays to a device: asks for the next block of float samples whenever it has room. A null device follows the system default speakers.</summary>
public sealed unsafe class MacOutputQueue : MacAudioQueue
{
    private readonly SpanAction _fill;
    private readonly OutputCallback _callback;

    public delegate void SpanAction(Span<float> block);

    public MacOutputQueue(string? deviceUid, int rate, int channels, int blockFrames, SpanAction fill, Action<Exception>? failed = null)
        : base(failed)
    {
        _fill = fill;
        _callback = OnBuffer;
        var format = FloatFormat(rate, channels);
        Check(AudioQueueNewOutput(ref format, _callback, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out Queue), "open the audio output");
        Prepare(deviceUid, blockFrames * channels * sizeof(float), Prime);
    }

    // Playback only starts once buffers are queued, so the first ones go in filled with silence.
    private void Prime(IntPtr buffer)
    {
        var b = (QueueBuffer*)buffer;
        new Span<byte>(b->Data, (int)b->Capacity).Clear();
        b->ByteSize = b->Capacity;
        Check(AudioQueueEnqueueBuffer(Queue, buffer, 0, IntPtr.Zero), "queue audio");
    }

    private void OnBuffer(IntPtr user, IntPtr queue, QueueBuffer* buffer)
    {
        if (!Running) return;
        try
        {
            _fill(new Span<float>(buffer->Data, (int)(buffer->Capacity / sizeof(float))));
            buffer->ByteSize = buffer->Capacity;
            AudioQueueEnqueueBuffer(queue, (IntPtr)buffer, 0, IntPtr.Zero);
        }
        catch (Exception ex) { Fail(ex); }
    }
}

public abstract class MacAudioQueue(Action<Exception>? failed) : IDisposable
{
    private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const uint FloatPacked = 1 | 8; // kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked

    /// <summary>Blocks kept queued. More = safer against glitches, fewer = less delay.</summary>
    public static int QueuedBuffers { get; set; } = 3;

    protected IntPtr Queue;
    protected volatile bool Running;

    protected static StreamFormat FloatFormat(int rate, int channels) => new()
    {
        SampleRate = rate,
        FormatId = MacAudioDevices.FourCC("lpcm"),
        Flags = FloatPacked,
        BytesPerPacket = (uint)(channels * sizeof(float)),
        FramesPerPacket = 1,
        BytesPerFrame = (uint)(channels * sizeof(float)),
        ChannelsPerFrame = (uint)channels,
        BitsPerChannel = 8 * sizeof(float),
    };

    protected void Prepare(string? deviceUid, int blockBytes, Action<IntPtr> queueBuffer)
    {
        if (deviceUid != null)
        {
            var uid = MacAudioDevices.CreateCFString(deviceUid);
            try { Check(AudioQueueSetProperty(Queue, MacAudioDevices.FourCC("aqcd"), ref uid, (uint)IntPtr.Size), "select the audio device"); }
            finally { MacAudioDevices.Release(uid); }
        }
        for (var i = 0; i < QueuedBuffers; i++)
        {
            Check(AudioQueueAllocateBuffer(Queue, (uint)blockBytes, out var buffer), "allocate audio buffers");
            queueBuffer(buffer);
        }
    }

    public void Start()
    {
        Running = true;
        Check(AudioQueueStart(Queue, IntPtr.Zero), "start audio");
    }

    protected void Fail(Exception ex)
    {
        Running = false;
        failed?.Invoke(ex);
    }

    protected static void Check(int status, string action)
    {
        if (status != 0) throw new InvalidOperationException($"Couldn't {action} (Core Audio error {status}).");
    }

    public void Dispose()
    {
        Running = false;
        if (Queue == IntPtr.Zero) return;
        AudioQueueStop(Queue, true);
        AudioQueueDispose(Queue, true);
        Queue = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    protected struct StreamFormat
    {
        public double SampleRate;
        public uint FormatId, Flags, BytesPerPacket, FramesPerPacket, BytesPerFrame, ChannelsPerFrame, BitsPerChannel, Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    protected unsafe struct QueueBuffer
    {
        public uint Capacity;
        public void* Data;
        public uint ByteSize;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    protected unsafe delegate void InputCallback(IntPtr user, IntPtr queue, QueueBuffer* buffer, IntPtr start, uint packets, IntPtr descriptions);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    protected unsafe delegate void OutputCallback(IntPtr user, IntPtr queue, QueueBuffer* buffer);

    [DllImport(AudioToolbox)]
    protected static extern int AudioQueueNewInput(ref StreamFormat format, InputCallback callback, IntPtr user, IntPtr runLoop, IntPtr runLoopMode, uint flags, out IntPtr queue);
    [DllImport(AudioToolbox)]
    protected static extern int AudioQueueNewOutput(ref StreamFormat format, OutputCallback callback, IntPtr user, IntPtr runLoop, IntPtr runLoopMode, uint flags, out IntPtr queue);
    [DllImport(AudioToolbox)]
    private static extern int AudioQueueAllocateBuffer(IntPtr queue, uint bytes, out IntPtr buffer);
    [DllImport(AudioToolbox)]
    protected static extern int AudioQueueEnqueueBuffer(IntPtr queue, IntPtr buffer, uint descriptions, IntPtr description);
    [DllImport(AudioToolbox)]
    private static extern int AudioQueueSetProperty(IntPtr queue, uint property, ref IntPtr value, uint size);
    [DllImport(AudioToolbox)]
    private static extern int AudioQueueStart(IntPtr queue, IntPtr startTime);
    [DllImport(AudioToolbox)]
    private static extern int AudioQueueStop(IntPtr queue, [MarshalAs(UnmanagedType.I1)] bool immediate);
    [DllImport(AudioToolbox)]
    private static extern int AudioQueueDispose(IntPtr queue, [MarshalAs(UnmanagedType.I1)] bool immediate);
}
