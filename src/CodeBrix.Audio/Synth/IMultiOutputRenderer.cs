using System;

namespace CodeBrix.Audio.Synth;

/// <summary>
/// A synthesizer that can render more than one stereo pair: a main output plus a number of auxiliary
/// stereo outputs.
/// </summary>
/// <remarks>
/// <para>
/// The Decent Sampler format lets a preset route a group, a zone or a bus to one of sixteen auxiliary
/// stereo outputs instead of, or as well as, the main output. A plug-in host offers those as extra
/// outputs; a library that only wants a stereo mix folds them back in.
/// </para>
/// <para>
/// <c>IMidiSynthesizer.Render</c> answers the second case: it produces the main output with the
/// auxiliary pairs folded in when <see cref="FoldAuxiliaryOutputs"/> is set, which is the default, and
/// without them when it is not. <see cref="RenderWithAuxiliary"/> answers the first: it never folds,
/// and hands each pair back separately whatever the flag says.
/// </para>
/// </remarks>
public interface IMultiOutputRenderer
{
    /// <summary>
    /// How many auxiliary stereo pairs the loaded instrument uses. Zero when it routes everything to
    /// the main output, which is the ordinary case.
    /// </summary>
    /// <remarks>
    /// This is the HIGHEST auxiliary output the instrument names, so a preset that uses
    /// <c>AUX_STEREO_OUTPUT_3</c> alone reports three and the first two pairs are silent.
    /// </remarks>
    int AuxiliaryOutputCount { get; }

    /// <summary>
    /// Whether <c>IMidiSynthesizer.Render</c> adds the auxiliary pairs into the stereo mix it
    /// returns. True by default, so nothing an instrument makes is lost when the consumer only has one
    /// pair of outputs.
    /// </summary>
    bool FoldAuxiliaryOutputs { get; set; }

    /// <summary>
    /// Renders one buffer, writing the main output into <paramref name="left"/> and
    /// <paramref name="right"/> and each auxiliary pair into its own buffers. The auxiliary pairs are
    /// never folded into the main output here, whatever <see cref="FoldAuxiliaryOutputs"/> says.
    /// </summary>
    /// <param name="left">The main output's left channel.</param>
    /// <param name="right">The main output's right channel. Same length as <paramref name="left"/>.</param>
    /// <param name="auxiliaryLeft">
    /// One buffer per auxiliary pair, each the same length as <paramref name="left"/>. May be shorter
    /// than <see cref="AuxiliaryOutputCount"/>, in which case the pairs beyond it are discarded, and
    /// may be null to discard every pair.
    /// </param>
    /// <param name="auxiliaryRight">The right channels, matching <paramref name="auxiliaryLeft"/>.</param>
    /// <exception cref="ArgumentException">The buffers are not all the same length.</exception>
    void RenderWithAuxiliary(
        Span<float> left, Span<float> right, float[][] auxiliaryLeft, float[][] auxiliaryRight);
}
