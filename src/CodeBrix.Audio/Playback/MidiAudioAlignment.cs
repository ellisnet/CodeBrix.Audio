using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Playback;

/// <summary>
/// Estimates how far a MIDI transcription sits from the audio it was transcribed from, by
/// cross-correlating the MIDI note-on times against an onset envelope of the audio.
/// </summary>
/// <remarks>
/// <para>
/// Machine transcriptions do not land exactly on the audio. The offset is constant across a
/// song - it does not drift - but it differs from song to song and from stem to stem within
/// one song, by as much as a fifth of a second either way. Aligning each MIDI track against
/// its OWN audio stem is therefore the only reliable approach; a single per-song number is a
/// fallback for a MIDI stem whose audio is missing.
/// </para>
/// <para>
/// The method: reduce the audio to an onset envelope sampled every millisecond (the RMS of
/// a trailing 20 ms window, then a half-wave-rectified first difference, so that what is
/// correlated is the moment energy RISES rather than how loud the material is); reduce the
/// MIDI to a train of unit impulses at the note-on times; slide one against the other over
/// <c>+/- maxOffsetSeconds</c> and take the highest normalised peak. Three choices in there
/// were settled by measurement against seven machine-transcribed songs rather than by taste:
/// a time-domain envelope rather than spectral flux, because it costs one pass over the
/// samples and the material this exists for - drums, percussion, bass - has onsets a
/// broadband energy rise already finds; a 20 ms window rather than 1 ms, because a
/// millisecond of a bass note is not a level, it is a piece of a waveform, and the envelope
/// then oscillates at the fundamental; and a LINEAR envelope rather than a log-compressed
/// one, because log compression emphasises the quiet beginning of an attack and pulled every
/// estimate 10 to 40 ms early.
/// </para>
/// <para>
/// Cost is roughly one second for a four-minute stem, nearly all of it in the envelope pass.
/// Nothing here touches an audio device and nothing is thread-affine, but it is far too slow
/// for a render callback: call it on a worker.
/// </para>
/// <para>
/// The search window matters. Widen it much past a beat and the estimator starts locking onto
/// beat multiples on periodic material - a confident, wrong answer. The default of 300 ms is
/// wide enough for every offset measured on real machine transcriptions and still well inside
/// one beat at any usual tempo. It is not immune: at a slow tempo an eighth note can fall
/// inside 300 ms, and on a stem whose pattern repeats exactly the estimator will happily
/// return that eighth note. A result whose winning peak sits on the very edge of the window
/// is reported with a confidence of zero, because the real peak may be just outside it. A
/// caller that knows the tempo should narrow the window to less than half the shortest
/// subdivision it expects; that is the only measure that removes the ambiguity rather than
/// arbitrating it.
/// </para>
/// <para>
/// Where several peaks ARE near-equal - within
/// <see cref="CandidatePeakFraction"/> of the tallest - the choice between them is not made on
/// height, because height is exactly what the teeth of a comb share. Each rival is scored a
/// second time on a much coarser question: smooth the stem's note density and the audio's
/// energy to a quarter of a second each and see how well the two agree at that lag. A beat's
/// rest in the part is a beat of quiet in the recording, and that survives smoothing while
/// individual onsets do not. A DECISIVE coarse win moves the answer, ties go to the smaller
/// offset, and an indecisive one leaves the tallest peak where it was. Either way the presence
/// of rivals lowers the confidence, because a comb of near-equal teeth is exactly the case
/// where no answer deserves to be applied without asking.
/// <see cref="MidiAudioAlignmentResult.CandidatePeakCount"/> says how many rivals there were.
/// </para>
/// </remarks>
public static class MidiAudioAlignment
{
    /// <summary>
    /// The default half-width of the offset search, in seconds. Wide enough for the offsets
    /// machine transcription produces, narrow enough to stay inside one beat at ordinary tempos.
    /// </summary>
    public const double DefaultMaxOffsetSeconds = 0.3;

    /// <summary>
    /// The largest search half-width accepted. Beyond about a second the search is guaranteed
    /// to cross several beats of any material and the answer stops meaning anything.
    /// </summary>
    public const double MaximumMaxOffsetSeconds = 2.0;

    /// <summary>
    /// Fewer note-ons than this and no estimate is called reliable, however clean the peak.
    /// </summary>
    public const int MinimumNoteOnsForReliability = 8;

    /// <summary>
    /// The <see cref="MidiAudioAlignmentResult.Confidence"/> at or above which
    /// <see cref="MidiAudioAlignmentResult.IsReliable"/> becomes true.
    /// </summary>
    public const double ReliableConfidence = 0.5;

    /// <summary>
    /// How tall a rival peak has to be, as a fraction of the tallest one, before the search
    /// treats it as a genuine alternative rather than as background. Rivals are separated on
    /// coarse envelope agreement rather than on height; see the remarks on the type.
    /// </summary>
    public const double CandidatePeakFraction = 0.6;

    /// <summary>
    /// The smoothing applied to both envelopes when rival peaks are separated: long enough that
    /// what is compared is the shape of the arrangement over several beats rather than the
    /// individual onsets the rivals already agree about.
    /// </summary>
    public const double CoarseAgreementSmoothingSeconds = 0.25;

    // The target envelope hop. The real hop is a whole number of samples, so the real bin rate
    // is sampleRate / hop and differs slightly from this at rates that are not a multiple of
    // 1000 - every conversion below uses the real one.
    private const double EnvelopeHopSeconds = 0.001;

    // The RMS window, as a multiple of the hop. It trails the bin, so a transient enters the
    // window in the bin it happens in and the first difference spikes there and nowhere else:
    // long enough to average out the waveform of a bass note, still sharp on a drum hit.
    private const int EnvelopeWindowBins = 20;

    // How far from the winning peak a competing peak has to be before it counts as a rival
    // rather than as the shoulder of the same peak.
    private const double RunnerUpExclusionSeconds = 0.020;

    // The peak-to-runner-up ratio that earns full confidence. Calibrated on machine
    // transcriptions of seven real songs: drum and percussion stems land between 1.5 and 4,
    // while pitched stems whose onsets the transcription only approximates sit between 1.0 and
    // 1.3. With ReliableConfidence at 0.5 the effective bar is a ratio of 1.5.
    private const double FullConfidenceRatio = 2.0;

    // How far apart two coarse agreement scores have to be before the choice between two rival
    // peaks counts as decisive. Coarse agreement is a Pearson correlation, so this is an
    // absolute difference: two rivals whose coarse shapes match the audio equally well leave
    // the answer ambiguous however tall either peak is, and the confidence says so.
    private const double DecisiveAgreementMargin = 0.05;

    // MIDI note-on status, channel bits stripped.
    private const byte NoteOnCommand = 0x90;

    /// <summary>
    /// Estimates the offset between a list of MIDI note-on times and a mono audio signal.
    /// </summary>
    /// <param name="noteOnTimesSeconds">
    /// The note-on times in seconds from the start of the sequence. Order does not matter and
    /// duplicates are harmless; note-ons landing in the same millisecond count once.
    /// </param>
    /// <param name="monoAudio">The audio to align against, as single-channel samples.</param>
    /// <param name="sampleRate">The sample rate of <paramref name="monoAudio"/>, in hertz.</param>
    /// <param name="maxOffsetSeconds">
    /// The half-width of the search, in seconds. The estimate is confined to
    /// <c>-maxOffsetSeconds .. +maxOffsetSeconds</c>.
    /// </param>
    /// <returns>
    /// The estimate. When there is nothing to work with - no note-ons, no audio, silence - the
    /// result is a zero offset with zero confidence rather than an exception.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="noteOnTimesSeconds"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sampleRate"/> is not positive, or <paramref name="maxOffsetSeconds"/> is
    /// not positive or exceeds <see cref="MaximumMaxOffsetSeconds"/>.
    /// </exception>
    public static MidiAudioAlignmentResult Estimate(IReadOnlyList<double> noteOnTimesSeconds,
        ReadOnlySpan<float> monoAudio, int sampleRate, double maxOffsetSeconds = DefaultMaxOffsetSeconds)
    {
        if (noteOnTimesSeconds == null) { throw new ArgumentNullException(nameof(noteOnTimesSeconds)); }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate,
                "The sample rate must be a positive number of hertz.");
        }
        if (maxOffsetSeconds <= 0 || maxOffsetSeconds > MaximumMaxOffsetSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOffsetSeconds), maxOffsetSeconds,
                $"The search half-width must be greater than zero and no more than {MaximumMaxOffsetSeconds} seconds.");
        }

        int hop = Math.Max(1, (int)Math.Round(sampleRate * EnvelopeHopSeconds));
        double binSeconds = (double)hop / sampleRate;
        int binCount = monoAudio.Length / hop;
        if (binCount < 4) { return Empty(0); }

        float[] flux = BuildOnsetFlux(monoAudio, hop, binCount, out float[] envelope);
        int[] onsetBins = BuildOnsetBins(noteOnTimesSeconds, binSeconds, binCount);
        if (onsetBins.Length == 0) { return Empty(0); }

        return Correlate(flux, envelope, onsetBins, binSeconds, maxOffsetSeconds);
    }

    /// <summary>
    /// Estimates the offset between a MIDI sequence's note-ons and a mono audio signal.
    /// </summary>
    /// <param name="sequence">The sequence whose note-on times are used.</param>
    /// <param name="monoAudio">The audio to align against, as single-channel samples.</param>
    /// <param name="sampleRate">The sample rate of <paramref name="monoAudio"/>, in hertz.</param>
    /// <param name="maxOffsetSeconds">The half-width of the search, in seconds.</param>
    /// <returns>The estimate; see the list overload.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sequence"/> is null.</exception>
    public static MidiAudioAlignmentResult Estimate(MidiSequence sequence,
        ReadOnlySpan<float> monoAudio, int sampleRate, double maxOffsetSeconds = DefaultMaxOffsetSeconds)
    {
        if (sequence == null) { throw new ArgumentNullException(nameof(sequence)); }
        return Estimate(GetNoteOnTimesSeconds(sequence), monoAudio, sampleRate, maxOffsetSeconds);
    }

    /// <summary>
    /// Estimates the offset against interleaved multi-channel audio, downmixing it to mono first.
    /// </summary>
    /// <param name="noteOnTimesSeconds">The note-on times in seconds.</param>
    /// <param name="interleavedAudio">
    /// The audio to align against, with channels interleaved frame by frame.
    /// </param>
    /// <param name="channels">How many channels <paramref name="interleavedAudio"/> carries.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <param name="maxOffsetSeconds">The half-width of the search, in seconds.</param>
    /// <returns>The estimate; see the mono overload.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channels"/> is not positive.</exception>
    public static MidiAudioAlignmentResult EstimateInterleaved(IReadOnlyList<double> noteOnTimesSeconds,
        ReadOnlySpan<float> interleavedAudio, int channels, int sampleRate,
        double maxOffsetSeconds = DefaultMaxOffsetSeconds)
    {
        if (channels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels,
                "The channel count must be a positive number.");
        }
        if (channels == 1)
        {
            return Estimate(noteOnTimesSeconds, interleavedAudio, sampleRate, maxOffsetSeconds);
        }
        return Estimate(noteOnTimesSeconds, Downmix(interleavedAudio, channels), sampleRate, maxOffsetSeconds);
    }

    /// <summary>
    /// Estimates the offset between a MIDI sequence and interleaved multi-channel audio,
    /// downmixing the audio to mono first.
    /// </summary>
    /// <param name="sequence">The sequence whose note-on times are used.</param>
    /// <param name="interleavedAudio">The audio, with channels interleaved frame by frame.</param>
    /// <param name="channels">How many channels <paramref name="interleavedAudio"/> carries.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <param name="maxOffsetSeconds">The half-width of the search, in seconds.</param>
    /// <returns>The estimate; see the mono overload.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sequence"/> is null.</exception>
    public static MidiAudioAlignmentResult EstimateInterleaved(MidiSequence sequence,
        ReadOnlySpan<float> interleavedAudio, int channels, int sampleRate,
        double maxOffsetSeconds = DefaultMaxOffsetSeconds)
    {
        if (sequence == null) { throw new ArgumentNullException(nameof(sequence)); }
        return EstimateInterleaved(GetNoteOnTimesSeconds(sequence), interleavedAudio, channels,
            sampleRate, maxOffsetSeconds);
    }

    /// <summary>
    /// Reads the note-on times out of a sequence, in seconds from its start.
    /// </summary>
    /// <param name="sequence">The sequence to read.</param>
    /// <returns>
    /// The times of every note-on with a non-zero velocity, in the order the sequence holds
    /// them (which is ascending time). A note-on with velocity zero is a note-off and is not
    /// included.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="sequence"/> is null.</exception>
    public static IReadOnlyList<double> GetNoteOnTimesSeconds(MidiSequence sequence)
    {
        if (sequence == null) { throw new ArgumentNullException(nameof(sequence)); }

        var messages = sequence.Messages;
        var times = sequence.Times;
        var result = new List<double>();
        for (int i = 0; i < messages.Length; i++)
        {
            var message = messages[i];
            if (message.Type != MidiSequence.MessageType.Normal) { continue; }
            if (message.Command != NoteOnCommand || message.Data2 == 0) { continue; }
            result.Add(times[i].TotalSeconds);
        }
        return result;
    }

    /// <summary>
    /// Decodes an audio file to mono floating-point samples at the file's own sample rate,
    /// using the ordinary readers (so WAV, MP3, Ogg Vorbis and FLAC all work, and MP3 arrives
    /// already trimmed of its encoder delay).
    /// </summary>
    /// <param name="path">The audio file to decode.</param>
    /// <param name="sampleRate">Receives the file's sample rate in hertz.</param>
    /// <returns>The decoded samples, averaged across channels.</returns>
    /// <remarks>
    /// The whole file lands in memory: a four-minute stereo stem is about 45 MB of mono float.
    /// That is fine for aligning one stem at a time on a worker and wrong for holding several
    /// at once - decode, estimate, drop.
    /// </remarks>
    internal static float[] ReadMonoSamples(string path, out int sampleRate)
    {
        using var reader = new AudioFileReader(path);
        sampleRate = reader.WaveFormat.SampleRate;
        int channels = Math.Max(1, reader.WaveFormat.Channels);

        long frameHint = reader.Length / (4L * channels);
        var accumulated = new List<float>(frameHint > 0 && frameHint < int.MaxValue ? (int)frameHint : 0);
        var buffer = new float[channels * 4096];
        int read;
        while ((read = reader.Read(buffer.AsSpan())) > 0)
        {
            for (int i = 0; i + channels <= read; i += channels)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++) { sum += buffer[i + c]; }
                accumulated.Add(sum / channels);
            }
        }
        return accumulated.ToArray();
    }

    private static MidiAudioAlignmentResult Empty(int noteOnCount) =>
        new MidiAudioAlignmentResult(0.0, 0.0, 0.0, false, noteOnCount, 0.0, 0);

    private static float[] Downmix(ReadOnlySpan<float> interleaved, int channels)
    {
        int frames = interleaved.Length / channels;
        var mono = new float[frames];
        int index = 0;
        for (int f = 0; f < frames; f++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++) { sum += interleaved[index++]; }
            mono[f] = sum / channels;
        }
        return mono;
    }

    // Trailing-window RMS at one bin per millisecond, then a half-wave-rectified first
    // difference. The result is zero everywhere the signal is steady or fading and positive
    // only where energy rises, which is what a note-on impulse should line up with.
    private static float[] BuildOnsetFlux(ReadOnlySpan<float> mono, int hop, int binCount,
        out float[] envelope)
    {
        int windowSamples = Math.Min(mono.Length, hop * EnvelopeWindowBins);
        envelope = new float[binCount];
        double sumOfSquares = 0.0;
        int windowStart = 0;
        for (int b = 0; b < binCount; b++)
        {
            int end = (b + 1) * hop;
            for (int i = b * hop; i < end; i++)
            {
                double sample = mono[i];
                sumOfSquares += sample * sample;
            }
            int newStart = Math.Max(0, end - windowSamples);
            for (; windowStart < newStart; windowStart++)
            {
                double sample = mono[windowStart];
                sumOfSquares -= sample * sample;
            }

            // Rounding in the running sum can leave a whisper of negative energy in silence.
            if (sumOfSquares < 0.0) { sumOfSquares = 0.0; }
            envelope[b] = (float)Math.Sqrt(sumOfSquares / (end - windowStart));
        }

        var flux = new float[binCount];
        for (int b = 1; b < binCount; b++)
        {
            float rise = envelope[b] - envelope[b - 1];
            flux[b] = rise > 0f ? rise : 0f;
        }
        return flux;
    }

    // Note-on times to bin indices, collapsed and sorted. Times outside the audio are kept:
    // they simply fall outside the array for most lags and are skipped there.
    private static int[] BuildOnsetBins(IReadOnlyList<double> noteOnTimesSeconds, double binSeconds, int binCount)
    {
        var bins = new List<int>(noteOnTimesSeconds.Count);
        for (int i = 0; i < noteOnTimesSeconds.Count; i++)
        {
            double time = noteOnTimesSeconds[i];
            if (double.IsNaN(time) || double.IsInfinity(time)) { continue; }
            double binValue = Math.Floor(time / binSeconds);

            // An onset that could never overlap the audio at any lag in range contributes
            // nothing but does inflate the note count, so drop the hopelessly out of range.
            if (binValue <= -binCount || binValue >= (2L * binCount)) { continue; }
            bins.Add((int)binValue);
        }

        var array = bins.ToArray();
        Array.Sort(array);
        int unique = 0;
        for (int i = 0; i < array.Length; i++)
        {
            if (i == 0 || array[i] != array[i - 1]) { array[unique++] = array[i]; }
        }
        Array.Resize(ref array, unique);
        return array;
    }

    private static MidiAudioAlignmentResult Correlate(float[] flux, float[] envelope, int[] onsetBins,
        double binSeconds, double maxOffsetSeconds)
    {
        int binCount = flux.Length;
        double mean = 0.0;
        for (int b = 0; b < binCount; b++) { mean += flux[b]; }
        mean /= binCount;

        double variance = 0.0;
        for (int b = 0; b < binCount; b++)
        {
            double d = flux[b] - mean;
            variance += d * d;
        }
        double standardDeviation = Math.Sqrt(variance / binCount);
        if (standardDeviation <= double.Epsilon) { return Empty(onsetBins.Length); }

        int maxLagBins = Math.Max(1, (int)Math.Round(maxOffsetSeconds / binSeconds));
        int lagCount = (2 * maxLagBins) + 1;
        var scores = new double[lagCount];
        for (int i = 0; i < lagCount; i++)
        {
            int lag = i - maxLagBins;
            double sum = 0.0;
            int used = 0;
            for (int n = 0; n < onsetBins.Length; n++)
            {
                int j = onsetBins[n] + lag;
                if (j < 0 || j >= binCount) { continue; }
                sum += flux[j];
                used++;
            }
            scores[i] = used == 0
                ? 0.0
                : (sum - (used * mean)) / (Math.Sqrt((double)binCount * used) * standardDeviation);
        }

        int tallestIndex = 0;
        for (int i = 1; i < lagCount; i++)
        {
            if (scores[i] > scores[tallestIndex]) { tallestIndex = i; }
        }
        double tallest = scores[tallestIndex];

        int exclusion = Math.Max(1, (int)Math.Round(RunnerUpExclusionSeconds / binSeconds));

        // Peaks that are near-equal in height cannot be separated by height. On periodic
        // material - a drum pattern, above all - they are the teeth of one comb a beat or a
        // subdivision apart, and the tallest tooth is not necessarily the right one. Ask a
        // second, independent question of each of them: over a quarter of a second at a time,
        // where does this stem PLAY, and does that agree with where the audio has energy?
        int[] candidates = FindCandidatePeaks(scores, tallest, exclusion);
        int peakIndex = tallestIndex;
        double agreementMargin = double.PositiveInfinity;

        if (candidates.Length > 1)
        {
            peakIndex = ChooseByCoarseAgreement(candidates, envelope, onsetBins, maxLagBins,
                binSeconds, out agreementMargin);
        }

        double peak = scores[peakIndex];

        double runnerUp = 0.0;
        bool haveRunnerUp = false;
        for (int i = 0; i < lagCount; i++)
        {
            if (Math.Abs(i - peakIndex) <= exclusion) { continue; }
            if (!haveRunnerUp || scores[i] > runnerUp)
            {
                runnerUp = scores[i];
                haveRunnerUp = true;
            }
        }

        // Confidence is about the peak that WON, and it is measured against everything else in the
        // window including the rivals. That is deliberate: a comb of near-equal teeth is exactly
        // the case where no answer deserves to be trusted, and the coarse envelope choosing one of
        // them is a preference, not a proof. So disambiguation moves the ANSWER and never raises
        // the confidence - a chosen tooth that was not the tallest scores below its own rival and
        // comes out unreliable, which is the honest report.
        double confidence = 0.0;
        if (peak > 0.0)
        {
            double ratio = runnerUp > 0.0 ? peak / runnerUp : double.PositiveInfinity;
            confidence = double.IsPositiveInfinity(ratio)
                ? 1.0
                : Math.Clamp((ratio - 1.0) / (FullConfidenceRatio - 1.0), 0.0, 1.0);

            // An indecisive choice between rivals is not worth full marks either.
            if (candidates.Length > 1)
            {
                confidence *= Math.Clamp(agreementMargin / DecisiveAgreementMargin, 0.0, 1.0);
            }
        }

        // A peak sitting on the edge of the search is not a peak, it is a wall: the real one
        // may be just outside. Report the lag, but never call it trustworthy.
        if (peakIndex == 0 || peakIndex == lagCount - 1) { confidence = 0.0; }

        double refinedLag = RefinePeak(scores, peakIndex) - maxLagBins;
        double offsetSeconds = refinedLag * binSeconds;
        bool isReliable = peak > 0.0
                          && confidence >= ReliableConfidence
                          && onsetBins.Length >= MinimumNoteOnsForReliability;

        return new MidiAudioAlignmentResult(offsetSeconds, peak, confidence, isReliable,
            onsetBins.Length, haveRunnerUp ? runnerUp : 0.0, Math.Max(1, candidates.Length));
    }

    // The local maxima that are tall enough to be worth arguing about, tallest first, with
    // anything inside the exclusion radius of a taller one suppressed as a shoulder of it.
    private static int[] FindCandidatePeaks(double[] scores, double tallest, int exclusion)
    {
        if (tallest <= 0.0) { return []; }

        double bar = tallest * CandidatePeakFraction;
        var found = new List<int>();
        for (int i = 0; i < scores.Length; i++)
        {
            if (scores[i] < bar) { continue; }
            if (i > 0 && scores[i - 1] > scores[i]) { continue; }
            if (i < scores.Length - 1 && scores[i + 1] > scores[i]) { continue; }
            found.Add(i);
        }

        found.Sort((left, right) => scores[right].CompareTo(scores[left]));

        var kept = new List<int>();
        foreach (int index in found)
        {
            bool shoulder = false;
            foreach (int already in kept)
            {
                if (Math.Abs(index - already) <= exclusion) { shoulder = true; break; }
            }
            if (!shoulder) { kept.Add(index); }
        }

        return kept.ToArray();
    }

    // Compares each rival lag on a much coarser question than the onset correlation asks: how
    // well does the stem's NOTE DENSITY over a quarter of a second at a time line up with how
    // much energy the audio has over the same quarter second? Two teeth of one comb score the
    // same on individual onsets by construction; they do not score the same on the shape of the
    // whole arrangement, because a beat's worth of silence in the part is a beat's worth of
    // quiet in the recording.
    private static int ChooseByCoarseAgreement(int[] candidates, float[] envelope, int[] onsetBins,
        int maxLagBins, double binSeconds, out double margin)
    {
        int binCount = envelope.Length;
        int smoothing = Math.Max(1, (int)Math.Round(CoarseAgreementSmoothingSeconds / binSeconds));

        var density = new float[binCount];
        foreach (int bin in onsetBins)
        {
            if (bin >= 0 && bin < binCount) { density[bin] = 1f; }
        }

        double[] coarseAudio = Smooth(envelope, smoothing);
        double[] coarseMidi = Smooth(density, smoothing);

        var agreements = new double[candidates.Length];
        for (int i = 0; i < candidates.Length; i++)
        {
            agreements[i] = Agreement(coarseMidi, coarseAudio, candidates[i] - maxLagBins);
        }

        // Ties go to the smaller offset: of two answers the evidence cannot separate, the one
        // that moves the music less is the safer one to apply.
        int bestAt = 0;
        for (int i = 1; i < candidates.Length; i++)
        {
            if (agreements[i] > agreements[bestAt] ||
                (agreements[i] == agreements[bestAt] &&
                 Math.Abs(candidates[i] - maxLagBins) < Math.Abs(candidates[bestAt] - maxLagBins)))
            {
                bestAt = i;
            }
        }

        double second = double.NegativeInfinity;
        for (int i = 0; i < candidates.Length; i++)
        {
            if (i != bestAt && agreements[i] > second) { second = agreements[i]; }
        }

        margin = double.IsNegativeInfinity(second) ? double.PositiveInfinity : agreements[bestAt] - second;

        // Only a DECISIVE coarse win moves the answer off the tallest peak. On material the
        // correlation cannot read at all - a soft-onset part whose whole curve is noise - there are
        // dozens of near-equal peaks and the coarse envelope separates them by nothing; picking one
        // of those would make the reported offset jump about between two recordings of the same
        // part. The confidence is lowered either way, which is the part that matters.
        return margin >= DecisiveAgreementMargin ? candidates[bestAt] : candidates[0];
    }

    // A centred moving average, in doubles because a quarter of a second of 1 ms bins is a long
    // running sum.
    private static double[] Smooth(float[] values, int halfWidth)
    {
        int count = values.Length;
        var prefix = new double[count + 1];
        for (int i = 0; i < count; i++) { prefix[i + 1] = prefix[i] + values[i]; }

        var smoothed = new double[count];
        for (int i = 0; i < count; i++)
        {
            int from = Math.Max(0, i - halfWidth);
            int to = Math.Min(count, i + halfWidth + 1);
            smoothed[i] = (prefix[to] - prefix[from]) / (to - from);
        }
        return smoothed;
    }

    // Pearson correlation of the two coarse envelopes with one slid by the given lag, over the
    // range where they overlap.
    private static double Agreement(double[] midi, double[] audio, int lag)
    {
        int count = midi.Length;
        int from = Math.Max(0, -lag);
        int to = Math.Min(count, count - lag);
        int used = to - from;
        if (used < 2) { return 0.0; }

        double sumA = 0.0, sumB = 0.0;
        for (int i = from; i < to; i++)
        {
            sumA += midi[i];
            sumB += audio[i + lag];
        }
        double meanA = sumA / used;
        double meanB = sumB / used;

        double covariance = 0.0, varianceA = 0.0, varianceB = 0.0;
        for (int i = from; i < to; i++)
        {
            double a = midi[i] - meanA;
            double b = audio[i + lag] - meanB;
            covariance += a * b;
            varianceA += a * a;
            varianceB += b * b;
        }

        double denominator = Math.Sqrt(varianceA * varianceB);
        return denominator <= double.Epsilon ? 0.0 : covariance / denominator;
    }

    // Parabolic interpolation through the peak and its two neighbours, so the answer is not
    // quantised to whole milliseconds. Clamped to half a bin: a fit that wants to move further
    // than that is fitting noise, not a peak.
    private static double RefinePeak(double[] scores, int peakIndex)
    {
        if (peakIndex <= 0 || peakIndex >= scores.Length - 1) { return peakIndex; }

        double left = scores[peakIndex - 1];
        double centre = scores[peakIndex];
        double right = scores[peakIndex + 1];
        double denominator = left - (2.0 * centre) + right;
        if (Math.Abs(denominator) <= double.Epsilon) { return peakIndex; }

        double shift = 0.5 * (left - right) / denominator;
        return peakIndex + Math.Clamp(shift, -0.5, 0.5);
    }
}
