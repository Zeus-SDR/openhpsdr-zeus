using Zeus.Plugins.Contracts.Audio;
using Zeus.Plugins.Host.Audio;

namespace Zeus.Plugins.Host.Tests;

/// <summary>
/// Realtime-contract regression: AudioChain.Process should not
/// allocate on the steady-state hot path. We don't enforce
/// zero-allocation across the whole call here (the chain's slot
/// array allocates lazily on construction), but we verify that
/// running the same block N times in a row produces zero GC events.
/// </summary>
public class AudioChainProcessNoAllocTests
{
    [Fact]
    public void Process_OverManyBlocks_DoesNotTriggerGen0Gc()
    {
        var chain = new AudioChain();
        var input  = new float[256];
        var output = new float[256];
        var ctx = new AudioBlockContext(48000, 1, 256, 0, false);
        for (int i = 0; i < input.Length; i++) input[i] = (float)i / 256f;

        // Warm-up: prime any one-shot init that AudioChain does on
        // first call.
        for (int i = 0; i < 16; i++) chain.Process(input, output, ctx);

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        var startGen0 = GC.CollectionCount(0);

        for (int i = 0; i < 10_000; i++)
        {
            chain.Process(input, output, ctx);
        }

        var endGen0 = GC.CollectionCount(0);
        Assert.Equal(startGen0, endGen0);
    }
}
