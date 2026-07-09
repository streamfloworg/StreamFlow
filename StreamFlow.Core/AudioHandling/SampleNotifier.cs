using System.Reactive.Linq;
using System.Runtime.InteropServices;

using StreamFlow.Core.Contracts;

using SoundFlow.Providers;

namespace StreamFlow.Core.AudioHandling;
internal class SampleNotifier : IDisposable
{
    /// <summary>
    /// Takes an IAbstractedWaveStream and notifies about samples read
    /// </summary>
    /// <param name="source"></param>
    public SampleNotifier(AssetDataProvider source)
    {
        _source = source;
    }

    private readonly AssetDataProvider _source;

    public int Read(Span<float> buffer)
    {
        var read = _source.ReadBytes(buffer);
        //var returnBuffer = MemoryMarshal.Cast<float, byte>(buffer).ToArray();
        //var aLeft = buffer.ToArray().Where((x, i) => i % 2 == 0).ToArray();
        //var aRight = buffer.ToArray().Where((x, i) => i % 2 != 0).ToArray();
        //var sortedLeft = MergeSort(aLeft, false);
        //var sortedRight = MergeSort(aRight, false);
        //maxLeft = sortedLeft[0];
        //maxRight = sortedRight[0];
        return read;
    }

    public long Length => _source.Length;
    public long Position
    {
        get; set;
    }



    public void Dispose()
    {
        _source.Dispose();
    }
}
