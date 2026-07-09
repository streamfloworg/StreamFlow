using System;
using System.Diagnostics;

namespace StreamFlow.Core.AudioProperties;

[DebuggerDisplay("Name: {Name} - Color: {Color}")]
public class CategoryBase
{

    public override int GetHashCode()
    {
        throw new NotImplementedException();
    }
}