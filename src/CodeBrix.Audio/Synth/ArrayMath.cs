using System;
using System.Numerics;
using System.Runtime.InteropServices;

// ReSharper disable once CheckNamespace
namespace CodeBrix.Audio.Synth; //was previously: MeltySynth

internal static class ArrayMath
{
    public static void MultiplyAdd(float a, float[] x, float[] destination)
    {
        /*
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] += a * x[i];
        }
        */

        var vx = MemoryMarshal.Cast<float, Vector<float>>(x);
        // net10 adaptation: .AsSpan() forces the mutable Span<T> overload of
        // MemoryMarshal.Cast; C# 13 first-class-span rules otherwise bind the
        // float[] argument to the ReadOnlySpan overload, breaking the vd[i] write below.
        var vd = MemoryMarshal.Cast<float, Vector<float>>(destination.AsSpan());

        var count = 0;

        for (var i = 0; i < vd.Length; i++)
        {
            vd[i] += a * vx[i];
            count += Vector<float>.Count;
        }

        for (var i = count; i < destination.Length; i++)
        {
            destination[i] += a * x[i];
        }
    }

    public static void MultiplyAdd(float a, float step, float[] x, float[] destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] += a * x[i];
            a += step;
        }
    }
}
