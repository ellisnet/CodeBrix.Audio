namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// THE TWO VOICE PROGRAMS, 052 Choir Aahs and 053 Voice Oohs, in each of the three readings
// GmChoirVoicing offers. They live here rather than in GmProgramRows because there are six rows
// instead of two, and because they are the one place in the bank where the same program is written
// more than once.
//
// HOW TO RETUNE THEM. Everything except the vowel and the breath is ordinary row data - the unison
// that decides how many people are singing, the detune and spread that decide how far apart they
// stand, the envelope that decides how the note is taken and let go, the filter that decides how
// bright the room is. The vowel is SHAPE, from a closed "oo" at 0 to an open "ah" at 1, and the
// breath is RING, 1 being the ordinary amount; both are read by GmChoirSpec.
//
// TO MAKE A DIFFERENT READING THE ONE THE BANK SINGS, change BankVoicing below. That is the whole
// edit - no API, no test, no other file - and every reading is already calibrated onto the same
// held-middle-C loudness, so nothing has to be re-measured.
//
// THE LEVELS, AND WHY 053's SIT BELOW THE BANK'S HELD-NOTE FIGURE. Every row here was brought onto
// the loudest hundred milliseconds of a held middle C at velocity 100, the way the whole bank was;
// 052's three readings are on it. All three of 053's are then trimmed a decibel and a half BELOW
// it, together, because a held note is not how a listener meets an "oo": on the sustained chord the
// ensemble audition plays, the untrimmed rows stood two and a half decibels above the family around
// them. The trim is applied to all three equally, so the readings stay matched to each other, and
// it follows the same rule as the three trimmed rows in GmProgramRows. IF YOU RETUNE ONE OF THEM,
// measure it on its phrase as well as on the held note.
internal static class GmChoirRows
{
    // WHICH READING THE BANK CARRIES. One constant, and it is the only thing that has to move.
    internal const GmChoirVoicing BankVoicing = GmChoirVoicing.Breathy;

    // WHICH READING GeneralMidiEnsemble.Full ASKS FOR - the larger section, twice the singers.
    internal const GmChoirVoicing LargerSection = GmChoirVoicing.Massed;

    // The two programs built on the choir voice.
    internal const int ChoirAahsProgram = 52;

    internal const int VoiceOohsProgram = 53;

    // How many readings there are, for the cache that holds one voicing each.
    internal const int VoicingCount = 3;

    internal static bool IsChoirProgram(int program) =>
        program == ChoirAahsProgram || program == VoiceOohsProgram;

    // Whether this program has a larger section to offer. It is the same two programs today, and
    // this is the one place to widen when another program grows one.
    internal static bool HasLargerSection(int program) =>
        LargerSection != BankVoicing && IsChoirProgram(program);

    // Where a choir program sits in the two-entry caches that hold the readings the bank does not
    // itself carry.
    internal static int ChoirIndex(int program) => program == ChoirAahsProgram ? 0 : 1;

    internal static GmRow Row(int program, GmChoirVoicing voicing) =>
        program == VoiceOohsProgram ? Oohs(voicing) : Aahs(voicing);

    private static GmRow Aahs(GmChoirVoicing voicing)
    {
        switch (voicing)
        {
            case GmChoirVoicing.Section: return SectionAahs();
            case GmChoirVoicing.Breathy: return BreathyAahs();
            default: return MassedAahs();
        }
    }

    private static GmRow Oohs(GmChoirVoicing voicing)
    {
        switch (voicing)
        {
            case GmChoirVoicing.Section: return SectionOohs();
            case GmChoirVoicing.Breathy: return BreathyOohs();
            default: return MassedOohs();
        }
    }

    // 052 Choir Aahs AS THE BANK SINGS IT: six singers on every key, in two groups that do not
    // quite shape the vowel the same way, standing well apart, with plenty of air, a rounded darker
    // vowel and a soft top.
    private static GmRow MassedAahs() =>
        new GmRow
        {
            Tone = GmTone.ChoirVowel, Shape = 0.72, Ring = 2.1,
            Unison = 3, Detune = 12.0, Spread = 0.95,
            Attack = 0.3, Decay = 1.4, Sustain = 0.94, Release = 0.95,
            Cutoff = 3000.0, KeyTracking = 0.2, VelocityOctaves = 0.7,
            VibratoRate = 4.9, VibratoDepth = 12.0, VibratoDelay = 0.55, VibratoFade = 0.9,
            PitchEnvelope = -0.14, PitchEnvelopeTime = 0.1,
            Layer2 = new GmLayerRow
            {
                Tone = GmTone.ChoirVowel, Shape = 0.6, Ring = 2.4, Level = 0.9,
                Unison = 3, Detune = 18.0, Spread = 0.55, Fine = 5.0,
            },
            Level = 0.0224, VelocityToLevel = 0.7, VelocityToAttack = 0.35,
            Reverb = 0.75, Chorus = 0.25,
        };

    // The same air and the same vowel with HALF THE PEOPLE - the reading that says how much a
    // massed choir owes to its head count and how much to the air and the vowel.
    private static GmRow BreathyAahs() =>
        new GmRow
        {
            Tone = GmTone.ChoirVowel, Shape = 0.72, Ring = 2.1,
            Unison = 3, Detune = 12.0, Spread = 0.95,
            Attack = 0.3, Decay = 1.4, Sustain = 0.94, Release = 0.95,
            Cutoff = 3000.0, KeyTracking = 0.2, VelocityOctaves = 0.7,
            VibratoRate = 4.9, VibratoDepth = 12.0, VibratoDelay = 0.55, VibratoFade = 0.9,
            PitchEnvelope = -0.14, PitchEnvelopeTime = 0.1,
            Level = 0.0281, VelocityToLevel = 0.7, VelocityToAttack = 0.35,
            Reverb = 0.75, Chorus = 0.25,
        };

    // A close section of three on the fully open vowel, with only a little air and a bright room.
    private static GmRow SectionAahs() =>
        new GmRow
        {
            Tone = GmTone.ChoirVowel, Shape = 1.0, Ring = 1.0,
            Unison = 3, Detune = 6.5, Spread = 0.8,
            Attack = 0.22, Decay = 1.4, Sustain = 0.94, Release = 0.8,
            Cutoff = 4400.0, KeyTracking = 0.2, VelocityOctaves = 0.7,
            VibratoRate = 4.9, VibratoDepth = 12.0, VibratoDelay = 0.55, VibratoFade = 0.9,
            PitchEnvelope = -0.14, PitchEnvelopeTime = 0.1,
            Level = 0.0303, VelocityToLevel = 0.7, VelocityToAttack = 0.35,
            Reverb = 0.7, Chorus = 0.15,
        };

    // 053 Voice Oohs AS THE BANK SINGS IT - the same six people with the mouth nearly shut.
    private static GmRow MassedOohs() =>
        new GmRow
        {
            Tone = GmTone.ChoirVowel, Shape = 0.0, Ring = 1.8,
            Unison = 3, Detune = 10.0, Spread = 0.9,
            Attack = 0.26, Decay = 1.4, Sustain = 0.94, Release = 0.85,
            Cutoff = 2400.0, KeyTracking = 0.2, VelocityOctaves = 0.6,
            VibratoRate = 4.7, VibratoDepth = 12.0, VibratoDelay = 0.55, VibratoFade = 0.9,
            PitchEnvelope = -0.12, PitchEnvelopeTime = 0.1,
            Layer2 = new GmLayerRow
            {
                Tone = GmTone.ChoirVowel, Shape = 0.14, Ring = 2.0, Level = 0.9,
                Unison = 3, Detune = 15.0, Spread = 0.5, Fine = -4.0,
            },
            Level = 0.0196, VelocityToLevel = 0.7, VelocityToAttack = 0.3,
            Reverb = 0.72, Chorus = 0.25,
        };

    private static GmRow BreathyOohs() =>
        new GmRow
        {
            Tone = GmTone.ChoirVowel, Shape = 0.0, Ring = 1.8,
            Unison = 3, Detune = 10.0, Spread = 0.9,
            Attack = 0.26, Decay = 1.4, Sustain = 0.94, Release = 0.85,
            Cutoff = 2400.0, KeyTracking = 0.2, VelocityOctaves = 0.6,
            VibratoRate = 4.7, VibratoDepth = 12.0, VibratoDelay = 0.55, VibratoFade = 0.9,
            PitchEnvelope = -0.12, PitchEnvelopeTime = 0.1,
            Level = 0.0246, VelocityToLevel = 0.7, VelocityToAttack = 0.3,
            Reverb = 0.72, Chorus = 0.25,
        };

    // A close pair on a slightly more open "oo", with only a little air.
    private static GmRow SectionOohs() =>
        new GmRow
        {
            Tone = GmTone.ChoirVowel, Shape = 0.06, Ring = 0.8,
            Unison = 2, Detune = 5.5, Spread = 0.7,
            Attack = 0.2, Decay = 1.4, Sustain = 0.94, Release = 0.7,
            Cutoff = 3000.0, KeyTracking = 0.2, VelocityOctaves = 0.6,
            VibratoRate = 4.7, VibratoDepth = 12.0, VibratoDelay = 0.55, VibratoFade = 0.9,
            PitchEnvelope = -0.12, PitchEnvelopeTime = 0.1,
            Level = 0.0365, VelocityToLevel = 0.7, VelocityToAttack = 0.3,
            Reverb = 0.68, Chorus = 0.15,
        };
}
