namespace CodeBrix.Audio.ModestSynth.Effects.Internal;

/// <summary>
/// How an effect parameter name is matched: case, punctuation and the <c>FX_</c> prefix are all
/// noise, so they are folded away before anything is compared.
/// </summary>
/// <remarks>
/// <para>
/// The developer guide names the same quantity twice - once as an XML attribute (<c>modRate</c>)
/// and once as a binding parameter (<c>FX_MOD_RATE</c>) - and a preset may spell either in any
/// case. Folding both to <c>modrate</c> means one switch serves both, which is what the format's
/// own shared parameter table implies.
/// </para>
/// <para>
/// Folding allocates one string per call, so it belongs on the control path and never inside a
/// render loop. Nothing in this package calls it from <c>Process</c>.
/// </para>
/// </remarks>
internal static class ModestEffectParameters
{
    /// <summary>
    /// Folds a parameter name to its comparison form: lower case, letters and digits only, and
    /// without the <c>fx</c> prefix the binding names carry.
    /// </summary>
    /// <param name="name">The name as the caller spelled it.</param>
    /// <returns>The folded name, or null when nothing usable is left.</returns>
    internal static string Fold(string name)
    {
        if (string.IsNullOrEmpty(name)) { return null; }

        char[] buffer = new char[name.Length];
        int length = 0;

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];

            if (c >= 'A' && c <= 'Z') { buffer[length++] = (char)(c + 32); }
            else if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) { buffer[length++] = c; }
        }

        if (length == 0) { return null; }

        // "FX_MOD_RATE" and "modRate" are the same parameter; dropping the prefix is what makes
        // one name table serve both spellings. A name that is nothing but "fx" is not a parameter.
        if (length > 2 && buffer[0] == 'f' && buffer[1] == 'x')
        {
            return new string(buffer, 2, length - 2);
        }

        return new string(buffer, 0, length);
    }
}
