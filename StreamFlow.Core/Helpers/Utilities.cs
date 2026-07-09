using System;
using System.Windows.Media.Animation;

using Microsoft.Win32;

namespace StreamFlow.Core.Helpers;

public static class Utilities
{
    public static Task<bool> IsAppInstalled(string AppKey)
    {
        var result = false;
        var uKeys64 = @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
        var uKeys32 = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        var keys64 = Registry.LocalMachine.OpenSubKey(uKeys64);
        var keys32 = Registry.LocalMachine.OpenSubKey(uKeys32);
        var allKeys = keys64!.GetSubKeyNames().Concat(keys32!.GetSubKeyNames());
        result = allKeys!.Any(key => key.StartsWith(AppKey, StringComparison.CurrentCultureIgnoreCase));
        return Task.FromResult(result);
    }


	
	public static float[] CreateSine(int timeIndex, float frequency, float sampleRate)
    {
        var projectedLength = sampleRate * timeIndex;
        Span<float> sineArray = stackalloc float[(int)(projectedLength)];
        var number_of_samples = sampleRate * timeIndex; // 40 second worth of samples
        for (var sample_number = 0; sample_number < number_of_samples; sample_number++)
        {
            //Console.WriteLine($"Generating sample {sample_number} of {number_of_samples}");
            var time_in_seconds = sample_number / sampleRate;
            var sample = (float)Math.Sin(2 * Math.PI * frequency * time_in_seconds);
            //Console.WriteLine($"Sample value: {sample}");
            sineArray[sample_number] = sample;
        }
        return sineArray.ToArray();
    }

    //Creates a sinewave
    public static float[] GetSineWave(double freq, int durationMs, int sampleRate, float decibel)
    {
        var max = DB2Float(decibel);//short.MaxValue
        double fs = sampleRate; // sample freq
        var len = sampleRate * durationMs / 1000;
        var data16Bit = new float[len];
        for (var i = 0; i < len; i++)
        {
            var t = i / fs; // current time
            data16Bit[i] = (float)(Math.Sin(2 * Math.PI * t * freq) * max);
        }
        return data16Bit;
    }

    private static float DB2Float(double dB)
    {
        var times = Math.Pow(10, dB / 10);
        return (float)(float.MaxValue * times);
    }
}