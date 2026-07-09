using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StreamFlow.Core.Contracts;
public interface ISampleNotifier
{
    //
    // Summary:
    //     A sample has been detected
    event EventHandler<SampleEventArgs> Sample;
}

/// <summary>
/// Sample event arguments
/// </summary>
public class SampleEventArgs : EventArgs
{
    /// <summary>
    /// Left sample
    /// </summary>
    public float Left
    {
        get; set;
    }
    /// <summary>
    /// Right sample
    /// </summary>
    public float Right
    {
        get; set;
    }

    /// <summary>
    /// Constructor
    /// </summary>
    public SampleEventArgs(float left, float right)
    {
        Left = left;
        Right = right;
    }
}