using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StreamFlow.Core.AudioProperties;
public static class LoopPointExtensions
{
    public static TimeSpan Min(this List<LoopPoint> loopPoints)
    {
        var minValue = double.MaxValue;
        if (loopPoints.Count > 0)
        {
            loopPoints.ForEach(lp => minValue = Math.Min(minValue, lp.Min().TotalSeconds));
        }
        else
        {
            minValue = 0.0;
        }
        return TimeSpan.FromSeconds(minValue);
    }

    public static TimeSpan Max(this List<LoopPoint> loopPoints)
    {
        var maxValueInSeconds = double.MinValue;
        if (loopPoints.Count > 0)
        {
            loopPoints.ForEach(lp => maxValueInSeconds = Math.Max(maxValueInSeconds, lp.Max().TotalSeconds));
        }
        return TimeSpan.FromSeconds(maxValueInSeconds);
    }

    public static bool Contains(this List<LoopPoint> loopPoints, TimeSpan loopPoint)
    {
        var contains = false;
        loopPoints.ForEach(lp =>
        {
            if (!contains)
            {
                contains = lp.EndLoopSample.Equals(loopPoint) || lp.StartLoopSample.Equals(loopPoint);
            }
        });
        return contains;
    }
}
