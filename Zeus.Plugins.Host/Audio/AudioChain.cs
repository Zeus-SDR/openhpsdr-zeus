using Zeus.Plugins.Contracts.Audio;
using Zeus.Plugins.Contracts.Extensions;

namespace Zeus.Plugins.Host.Audio;

/// <summary>
/// Serial chain of <see cref="IAudioPlugin"/> instances with master
/// enable + per-slot bypass. The realtime <see cref="Process"/> method
/// allocates nothing, takes no locks, and short-circuits to a single
/// memcpy when master enable is false — matching the bit-identical
/// pass-through requirement from the v1 ADR (§5.7).
///
/// Slot mutation methods (<see cref="SetSlot"/>, <see cref="ClearSlot"/>,
/// <see cref="SetSlotBypass"/>) are NOT realtime-safe — call from the
/// control thread before / after a block, never from inside it.
/// </summary>
public sealed class AudioChain : IAsyncDisposable
{
    public const int MaxSlots = 8;

    private readonly ChainSlot[] _slots = new ChainSlot[MaxSlots];
    private readonly float[] _scratch;
    private volatile bool _masterEnabled = true;

    public AudioChain(int maxFrames = 4096, int maxChannels = 2)
    {
        _scratch = new float[maxFrames * maxChannels];
        for (int i = 0; i < MaxSlots; i++) _slots[i] = new ChainSlot();
    }

    public int SlotCount => MaxSlots;

    public bool MasterEnabled
    {
        get => _masterEnabled;
        set => _masterEnabled = value;
    }

    public IAudioPlugin? GetSlot(int index)
    {
        ValidateIndex(index);
        return _slots[index].Plugin;
    }

    public void SetSlot(int index, IAudioPlugin plugin)
    {
        ValidateIndex(index);
        var slot = _slots[index];
        slot.Plugin = plugin;
        slot.Bypassed = false;
    }

    public void ClearSlot(int index)
    {
        ValidateIndex(index);
        _slots[index].Plugin = null;
        _slots[index].Bypassed = false;
    }

    public bool IsSlotBypassed(int index)
    {
        ValidateIndex(index);
        return _slots[index].Bypassed;
    }

    public void SetSlotBypass(int index, bool bypassed)
    {
        ValidateIndex(index);
        _slots[index].Bypassed = bypassed;
    }

    /// <summary>
    /// Run the chain over one block. Input is read once into output;
    /// the chain then ping-pongs between <paramref name="output"/> and
    /// the internal scratch buffer to chain plugins without allocating
    /// per call.
    ///
    /// When master is disabled, this is a single <c>input.CopyTo(output)</c>
    /// and exits. Slots whose Plugin is null or Bypassed = true are
    /// skipped without a copy (handled by the ping-pong logic).
    /// </summary>
    public void Process(ReadOnlySpan<float> input, Span<float> output, AudioBlockContext ctx)
    {
        if (output.Length < input.Length)
            throw new ArgumentException("output too small", nameof(output));

        if (!_masterEnabled)
        {
            input.CopyTo(output);
            return;
        }

        var needed = ctx.Frames * ctx.Channels;
        if (needed > _scratch.Length)
        {
            // Block bigger than we sized for — fall back to safe pass-through
            input.CopyTo(output);
            return;
        }

        // Seed the chain by copying input into output; subsequent stages
        // ping-pong between `output` and `_scratch`.
        input.CopyTo(output);

        // Track which buffer (output vs scratch) is the most-recently-written
        // one without tuple-swapping Span<float> (which is a ref struct
        // and disallowed in tuples).
        bool currentIsOutput = true;

        for (int i = 0; i < MaxSlots; i++)
        {
            var slot = _slots[i];
            var plugin = slot.Plugin;
            if (plugin is null || slot.Bypassed) continue;

            if (currentIsOutput)
                plugin.Process(output[..needed], _scratch.AsSpan(0, needed), ctx);
            else
                plugin.Process(_scratch.AsSpan(0, needed), output[..needed], ctx);

            currentIsOutput = !currentIsOutput;
        }

        if (!currentIsOutput)
        {
            _scratch.AsSpan(0, needed).CopyTo(output);
        }
    }

    private static void ValidateIndex(int index)
    {
        if ((uint)index >= MaxSlots)
            throw new ArgumentOutOfRangeException(nameof(index), $"slot index must be in 0..{MaxSlots - 1}");
    }

    public async ValueTask DisposeAsync()
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            var p = _slots[i].Plugin;
            _slots[i].Plugin = null;
            if (p is IAsyncDisposable ad) await ad.DisposeAsync().ConfigureAwait(false);
            else if (p is IDisposable d) d.Dispose();
        }
    }

    private sealed class ChainSlot
    {
        public IAudioPlugin? Plugin;
        public bool Bypassed;
    }
}
