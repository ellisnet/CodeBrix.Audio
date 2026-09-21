namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// Which reading of the choir a synthesizer sings. All three are THE SAME VOICE - the same formants,
// the same wander, the same breath - set differently, so choosing between them is a matter of taste
// rather than of machinery, and making a different one the bank's own is an edit to one constant.
//
// It is INTERNAL, and it is chosen per synthesizer rather than process-wide on purpose: a global
// switch would mean one test flipping it could change what another test hears while both were
// running. The bank's own reading is GmChoirRows.BankVoicing, and it is deliberately the enum's
// ZERO, so a default-constructed value is what the bank sings.
internal enum GmChoirVoicing
{
    // WHAT THE BANK SINGS. Six singers, in two groups that do not quite shape the vowel the same
    // way, standing well apart, with plenty of breath, a rounded darker vowel and a soft top.
    Massed = 0,

    // The same darker vowel, the same breath, the same spacing - but THREE singers instead of six.
    // It costs half as much, and it is the reading that says how much of a massed choir is the
    // head count and how much is the air and the vowel.
    Breathy,

    // A close-packed section of three on the fully open vowel with only a little air in it, in a
    // bright room. Focused and defined - and, on a first hearing, still a shade organ-like, which
    // is why it is not the one the bank carries.
    Section,
}
