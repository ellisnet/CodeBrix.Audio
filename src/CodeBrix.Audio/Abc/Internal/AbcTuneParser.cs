using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Audio.Abc.Internal;

// Turns the lines of one abc tune into an AbcTune.
//
// The shape follows the standard: a tune header of information fields ending at the K: field, then
// a tune body of music code, information fields and stylesheet directives, ending at a blank line
// or the end of the file. Nothing here throws on content; every departure goes to the context, and
// each KIND of skipped construct is named once.
internal sealed class AbcTuneParser
{
    private readonly AbcParseContext _context = new AbcParseContext();
    private readonly List<AbcVoiceBuilder> _voices = [];
    private readonly Dictionary<string, AbcVoiceBuilder> _voicesById =
        new Dictionary<string, AbcVoiceBuilder>(StringComparer.Ordinal);

    private readonly List<string> _titles = [];

    private int _referenceNumber;
    private string _composer = string.Empty;
    private AbcMeter _meter = AbcMeter.Free;
    private AbcDuration _unitNoteLength = new AbcDuration(1, 8);
    private bool _unitNoteLengthWasSet;
    private AbcTempo _tempo;
    private AbcKey _key = AbcKey.CMajor;
    private AbcVoiceBuilder _currentVoice;
    private int? _headerProgram;
    private int? _headerChannel;

    // What was in force when the body began. The running _meter, _unitNoteLength and _key go on
    // changing as inline fields arrive, because the body needs them to read the notes that follow;
    // the TUNE reports the values it started with, and the changes are elements in the bars.
    private AbcMeter _startMeter = AbcMeter.Free;
    private AbcDuration _startUnitNoteLength = new AbcDuration(1, 8);
    private bool _startUnitNoteLengthWasSet;
    private AbcKey _startKey = AbcKey.CMajor;

    internal static AbcTune Parse(IReadOnlyList<string> lines)
    {
        var parser = new AbcTuneParser();
        return parser.Run(lines);
    }

    private AbcTune Run(IReadOnlyList<string> lines)
    {
        int index = 0;
        bool inBody = false;

        while (index < lines.Count)
        {
            string line = lines[index];
            index++;

            if (line.Length > 1 && line[0] == '%' && line[1] == '%')
            {
                ReadDirective(line.Substring(2).Trim());
                continue;
            }

            if (TryReadFieldLine(line, out char field, out string value) &&
                (!inBody || IsLegalBodyField(field)))
            {
                if (!inBody)
                {
                    // A field may be continued with "+:" on the following lines.
                    while (index < lines.Count && lines[index].StartsWith("+:", StringComparison.Ordinal))
                    {
                        value = value + " " + lines[index].Substring(2).Trim();
                        index++;
                    }

                    ReadHeaderField(field, value);
                    if (field == 'K')
                    {
                        inBody = true;
                        StartBody();
                    }

                    continue;
                }

                ReadBodyField(field, value);
                continue;
            }

            if (!inBody)
            {
                if (line.Trim().Length > 0)
                {
                    _context.AddOnce(
                        "header-junk",
                        "A line in the tune header that was not an information field was skipped.");
                }

                continue;
            }

            ReadMusicLine(line);
        }

        if (!inBody)
        {
            _context.Add("The tune has no K: field, so it has no tune body; nothing was read.");
            StartBody();
        }

        return Build();
    }

    // --- header -------------------------------------------------------------------------------

    private void ReadHeaderField(char field, string value)
    {
        switch (field)
        {
            case 'X':
                if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                {
                    _referenceNumber = number;
                }
                else
                {
                    _context.Add($"The reference number \"{value}\" is not a number; zero was used instead.");
                }

                return;

            case 'T':
                _titles.Add(value.Trim());
                return;

            case 'C':
                _composer = _composer.Length == 0 ? value.Trim() : _composer + "; " + value.Trim();
                return;

            case 'M':
                _meter = AbcFieldParser.ParseMeter(value, _context);
                return;

            case 'L':
                _unitNoteLength = AbcFieldParser.ParseUnitNoteLength(value, _context, _unitNoteLength);
                _unitNoteLengthWasSet = true;
                return;

            case 'Q':
                _tempo = AbcFieldParser.ParseTempo(value, CurrentUnitNoteLength(), _context);
                return;

            case 'K':
                _key = AbcFieldParser.ParseKey(value, _context);
                return;

            case 'V':
                DeclareVoice(value);
                return;

            case 'I':
                ReadInstruction(value);
                return;

            case 'r':
                return;

            default:
                ReportSkippedField(field);
                return;
        }
    }

    private void StartBody()
    {
        if (!_unitNoteLengthWasSet)
        {
            _unitNoteLength = AbcUnitNoteLength.DefaultFor(_meter);
        }

        // Everything the header declared belongs to the first voice, and the body starts there
        // rather than at whichever voice the header happened to mention last. A tune that declared
        // no voice gets one the moment the body writes a note - not here, because the body may open
        // with a V: field of its own, and creating one now would leave an empty voice in front of it.
        _currentVoice = _voices.Count > 0 ? _voices[0] : null;

        _startMeter = _meter;
        _startUnitNoteLength = _unitNoteLength;
        _startUnitNoteLengthWasSet = _unitNoteLengthWasSet;
        _startKey = _key;
    }

    // The header's %%MIDI directives belong to the first voice, whether it was declared in the
    // header or only appeared once the body started.
    private void ApplyHeaderDirectives(AbcVoiceBuilder voice)
    {
        if (_headerProgram.HasValue && !voice.MidiProgram.HasValue)
        {
            voice.MidiProgram = _headerProgram;
        }

        if (_headerChannel.HasValue && !voice.MidiChannel.HasValue)
        {
            voice.MidiChannel = _headerChannel;
        }
    }

    // --- body ---------------------------------------------------------------------------------

    private void ReadBodyField(char field, string value)
    {
        switch (field)
        {
            case 'K':
                {
                    var key = AbcFieldParser.ParseKey(value, _context);
                    _key = key;
                    AddInline(new AbcInlineField('K', value.Trim(), key, null, null, null));
                    return;
                }

            case 'M':
                {
                    var meter = AbcFieldParser.ParseMeter(value, _context);
                    _meter = meter;
                    AddInline(new AbcInlineField('M', value.Trim(), null, meter, null, null));
                    return;
                }

            case 'L':
                {
                    var length = AbcFieldParser.ParseUnitNoteLength(value, _context, _unitNoteLength);
                    _unitNoteLength = length;
                    _unitNoteLengthWasSet = true;
                    AddInline(new AbcInlineField('L', value.Trim(), null, null, new AbcUnitNoteLength(length, false), null));
                    return;
                }

            case 'Q':
                {
                    var tempo = AbcFieldParser.ParseTempo(value, CurrentUnitNoteLength(), _context);
                    if (_tempo == null)
                    {
                        _tempo = tempo;
                    }

                    AddInline(new AbcInlineField('Q', value.Trim(), null, null, null, tempo));
                    return;
                }

            case 'V':
                SwitchVoice(value);
                return;

            case 'I':
                ReadInstruction(value);
                return;

            case 'r':
                return;

            case 'T':
                _titles.Add(value.Trim());
                return;

            default:
                ReportSkippedField(field);
                return;
        }
    }

    private void ReadMusicLine(string line)
    {
        if (line.Trim().Length == 0)
        {
            return;
        }

        var tokens = AbcTokenizer.Tokenize(line);

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            // An inline V: field changes which voice the REST of the line belongs to, and must not
            // bring the default voice into being on its way past.
            if (token.Kind == AbcTokenKind.InlineField && token.Field == 'V')
            {
                SwitchVoice(token.Text);
                continue;
            }

            ReadToken(CurrentVoice(), token);
        }
    }

    private void ReadToken(AbcVoiceBuilder voice, AbcToken token)
    {
        switch (token.Kind)
        {
            case AbcTokenKind.Note:
                AddNote(voice, token);
                return;

            case AbcTokenKind.Rest:
                {
                    var length = _unitNoteLength * new AbcDuration(token.LengthNumerator, token.LengthDenominator);
                    length = ApplyPendingBroken(voice, length);
                    AddDurational(voice, new AbcRest(length, token.IsVisible, 0));
                    return;
                }

            case AbcTokenKind.MultiMeasureRest:
                {
                    AbcDuration bar = _meter.BarLength;
                    if (bar.IsZero)
                    {
                        _context.AddOnce(
                            "multi-measure-free-meter",
                            "A multi-measure rest was written in free meter, where a measure has no length; each measure was read as a whole note.");
                        bar = AbcDuration.Whole;
                    }

                    AddDurational(voice, new AbcRest(bar * token.MeasureCount, token.IsVisible, token.MeasureCount));
                    return;
                }

            case AbcTokenKind.ChordStart:
                if (voice.InChord)
                {
                    _context.AddOnce("chord-nesting", "A chord was opened inside another chord; the inner brackets were ignored.");
                    return;
                }

                voice.ChordNotes = [];
                return;

            case AbcTokenKind.ChordEnd:
                CloseChord(voice, token);
                return;

            case AbcTokenKind.GraceStart:
                if (voice.InGrace)
                {
                    _context.AddOnce("grace-nesting", "A grace group was opened inside another; the inner braces were ignored.");
                    return;
                }

                voice.GraceNotes = [];
                voice.GraceIsAcciaccatura = token.IsAcciaccatura;
                return;

            case AbcTokenKind.GraceEnd:
                CloseGrace(voice);
                return;

            case AbcTokenKind.TupletStart:
                OpenTuplet(voice, token);
                return;

            case AbcTokenKind.BarLine:
                CloseBar(voice, ParseBarLine(token.Text));
                return;

            case AbcTokenKind.Ending:
                if (token.Endings != null)
                {
                    for (int i = 0; i < token.Endings.Length; i++)
                    {
                        if (!voice.PendingEndings.Contains(token.Endings[i]))
                        {
                            voice.PendingEndings.Add(token.Endings[i]);
                        }
                    }
                }

                return;

            case AbcTokenKind.Tie:
                ApplyTie(voice);
                return;

            case AbcTokenKind.BrokenRhythm:
                ApplyBrokenRhythm(voice, token.BrokenRhythm);
                return;

            case AbcTokenKind.InlineField:
                ReadBodyField(token.Field, token.Text);
                return;

            case AbcTokenKind.SlurStart:
            case AbcTokenKind.SlurEnd:
                // Slurs say how to play, not how long; they change nothing that can be measured.
                return;

            case AbcTokenKind.Decoration:
                _context.AddOnce("decorations", "Decorations were ignored; they do not change which notes sound or for how long.");
                return;

            case AbcTokenKind.ChordSymbol:
                _context.AddOnce("chord-symbols", "Chord symbols were ignored; no accompaniment is generated from them.");
                return;

            case AbcTokenKind.Annotation:
                _context.AddOnce("annotations", "Text annotations were ignored.");
                return;

            case AbcTokenKind.LayoutMark:
                _context.AddOnce("layout-marks", "Layout marks were ignored; they affect the printed page only.");
                return;

            case AbcTokenKind.VoiceOverlay:
                _context.AddOnce(
                    "voice-overlay",
                    "The voice overlay operator '&' was ignored; the material after it was read as part of the same voice.");
                return;

            default:
                _context.AddOnce("unrecognised", "Characters that are not part of abc music code were ignored.");
                return;
        }
    }

    // --- elements -----------------------------------------------------------------------------

    private void AddNote(AbcVoiceBuilder voice, AbcToken token)
    {
        var written = new AbcDuration(token.LengthNumerator, token.LengthDenominator);

        if (voice.InGrace)
        {
            // A grace note's written length is recorded but its sounding length comes from the
            // conversion options, as the standard says it must.
            voice.GraceNotes.Add(new AbcNote(
                token.Letter, token.Octave, token.Accidental, _unitNoteLength * written, false));
            return;
        }

        if (voice.InChord)
        {
            voice.ChordNotes.Add(new AbcNote(
                token.Letter, token.Octave, token.Accidental, _unitNoteLength * written, false));
            return;
        }

        var length = ApplyPendingBroken(voice, _unitNoteLength * written);
        AddDurational(voice, new AbcNote(token.Letter, token.Octave, token.Accidental, length, false));
    }

    private void CloseChord(AbcVoiceBuilder voice, AbcToken token)
    {
        if (!voice.InChord)
        {
            _context.AddOnce("chord-close", "A closing ']' was found with no chord open; it was ignored.");
            return;
        }

        var notes = voice.ChordNotes;
        voice.ChordNotes = null;

        if (notes.Count == 0)
        {
            _context.AddOnce("empty-chord", "An empty chord was found; it was ignored.");
            return;
        }

        // The standard: where the notes differ in length the chord lasts as long as its FIRST note,
        // and a length written after the bracket MULTIPLIES what is inside it.
        var length = notes[0].Length * new AbcDuration(token.LengthNumerator, token.LengthDenominator);
        length = ApplyPendingBroken(voice, length);
        AddDurational(voice, new AbcChord(notes, length, false));
    }

    private void CloseGrace(AbcVoiceBuilder voice)
    {
        if (!voice.InGrace)
        {
            _context.AddOnce("grace-close", "A closing '}' was found with no grace group open; it was ignored.");
            return;
        }

        var notes = voice.GraceNotes;
        bool acciaccatura = voice.GraceIsAcciaccatura;
        voice.GraceNotes = null;
        voice.GraceIsAcciaccatura = false;

        if (notes.Count == 0)
        {
            return;
        }

        TargetList(voice).Add(new AbcGraceGroup(notes, acciaccatura));
    }

    private void OpenTuplet(AbcVoiceBuilder voice, AbcToken token)
    {
        if (voice.InTuplet)
        {
            CloseTuplet(voice);
            _context.AddOnce("tuplet-nesting", "A tuplet was opened before the previous one finished; the first was closed where the second began.");
        }

        int p = token.TupletP;
        if (p < 2)
        {
            _context.AddOnce("tuplet-size", "A tuplet with fewer than two notes was ignored.");
            return;
        }

        int q = token.TupletQ > 0 ? token.TupletQ : ImpliedTupletQ(p);
        int r = token.TupletR > 0 ? token.TupletR : p;

        voice.TupletP = p;
        voice.TupletQ = q;
        voice.TupletRemaining = r;
        voice.TupletCovered = r;
        voice.TupletElements = [];
    }

    // The standard's table: (2 is two in the time of three, (3 three in the time of two, (4 four in
    // the time of three, (6 six in the time of two, (8 eight in the time of three; and 5, 7 and 9
    // are in the time of three in a compound meter and two otherwise.
    private int ImpliedTupletQ(int p)
    {
        switch (p)
        {
            case 2: return 3;
            case 3: return 2;
            case 4: return 3;
            case 6: return 2;
            case 8: return 3;
            case 5:
            case 7:
            case 9:
                return _meter.IsCompound ? 3 : 2;
            default:
                return _meter.IsCompound ? 3 : 2;
        }
    }

    private void CloseTuplet(AbcVoiceBuilder voice)
    {
        if (!voice.InTuplet)
        {
            return;
        }

        var elements = voice.TupletElements;
        int p = voice.TupletP;
        int q = voice.TupletQ;
        int covered = voice.TupletCovered;
        voice.TupletElements = null;

        if (elements.Count == 0)
        {
            return;
        }

        var group = new AbcTupletGroup(p, q, covered, elements);
        voice.Current.Add(group);
        // A tie or a broken-rhythm marker after the tuplet looks at the last element INSIDE it.
        voice.LastList = null;
        voice.LastIndex = -1;
    }

    private List<AbcElement> TargetList(AbcVoiceBuilder voice) =>
        voice.InTuplet ? voice.TupletElements : voice.Current;

    private void AddDurational(AbcVoiceBuilder voice, AbcElement element)
    {
        var target = TargetList(voice);
        target.Add(element);
        voice.LastList = target;
        voice.LastIndex = target.Count - 1;

        if (voice.InTuplet)
        {
            voice.TupletRemaining--;
            if (voice.TupletRemaining <= 0)
            {
                CloseTuplet(voice);
            }
        }
    }

    private void AddInline(AbcInlineField field)
    {
        var voice = CurrentVoice();
        TargetList(voice).Add(field);
    }

    private AbcDuration ApplyPendingBroken(AbcVoiceBuilder voice, AbcDuration length)
    {
        if (voice.PendingBroken == AbcDuration.Whole)
        {
            return length;
        }

        var scaled = length * voice.PendingBroken;
        voice.PendingBroken = AbcDuration.Whole;
        return scaled;
    }

    private void ApplyTie(AbcVoiceBuilder voice)
    {
        if (voice.LastList == null || voice.LastIndex < 0 || voice.LastIndex >= voice.LastList.Count)
        {
            _context.AddOnce("tie-without-note", "A tie was written with no note before it; it was ignored.");
            return;
        }

        var element = voice.LastList[voice.LastIndex];
        if (element is AbcNote note)
        {
            voice.LastList[voice.LastIndex] =
                new AbcNote(note.Letter, note.Octave, note.Accidental, note.Length, true);
            return;
        }

        if (element is AbcChord chord)
        {
            voice.LastList[voice.LastIndex] = new AbcChord(chord.Notes, chord.Length, true);
            return;
        }

        _context.AddOnce("tie-without-note", "A tie was written after something that is not a note; it was ignored.");
    }

    // ">" dots the note before and halves the one after; ">>" and ">>>" go further, and "<" is the
    // same the other way round.
    private void ApplyBrokenRhythm(AbcVoiceBuilder voice, int marks)
    {
        int count = Math.Abs(marks);
        if (count == 0 || count > 8)
        {
            _context.AddOnce("broken-rhythm", "A broken-rhythm marker could not be read; it was ignored.");
            return;
        }

        long power = 1L << count;
        var longer = new AbcDuration((2 * power) - 1, power);
        var shorter = new AbcDuration(1, power);

        var first = marks > 0 ? longer : shorter;
        var second = marks > 0 ? shorter : longer;

        if (voice.LastList == null || voice.LastIndex < 0 || voice.LastIndex >= voice.LastList.Count)
        {
            _context.AddOnce("broken-rhythm", "A broken-rhythm marker was written with no note before it; it was ignored.");
            return;
        }

        var element = voice.LastList[voice.LastIndex];
        if (element is AbcNote note)
        {
            voice.LastList[voice.LastIndex] =
                new AbcNote(note.Letter, note.Octave, note.Accidental, note.Length * first, note.TiedToNext);
        }
        else if (element is AbcChord chord)
        {
            voice.LastList[voice.LastIndex] = new AbcChord(chord.Notes, chord.Length * first, chord.TiedToNext);
        }
        else if (element is AbcRest rest)
        {
            voice.LastList[voice.LastIndex] = new AbcRest(rest.Length * first, rest.IsVisible, rest.MeasureCount);
        }
        else
        {
            _context.AddOnce("broken-rhythm", "A broken-rhythm marker was written after something that is not a note; it was ignored.");
            return;
        }

        voice.PendingBroken = second;
    }

    // --- bars ---------------------------------------------------------------------------------

    private static AbcBarLine ParseBarLine(string text)
    {
        bool startsRepeat = false;
        bool endsRepeat = false;
        int leadingDots = 0;
        int trailingDots = 0;

        int first = 0;
        while (first < text.Length && text[first] == ':')
        {
            leadingDots++;
            first++;
        }

        int last = text.Length - 1;
        while (last >= first && text[last] == ':')
        {
            trailingDots++;
            last--;
        }

        // Dots before the line end a repeat; dots after it start one. "::" is both.
        endsRepeat = leadingDots > 0;
        startsRepeat = trailingDots > 0;

        string core = text.Substring(first, Math.Max(0, last - first + 1));

        // "::" on its own has no line between the dots at all.
        if (core.Length == 0 && leadingDots > 0 && trailingDots == 0 && leadingDots > 1)
        {
            endsRepeat = true;
            startsRepeat = true;
            leadingDots = leadingDots / 2 > 0 ? leadingDots / 2 : 1;
            trailingDots = leadingDots;
        }

        AbcBarLineKind kind;
        if (text.StartsWith(".", StringComparison.Ordinal))
        {
            kind = AbcBarLineKind.Dotted;
        }
        else if (core == "[|]")
        {
            kind = AbcBarLineKind.Invisible;
        }
        else if (core == "|]")
        {
            kind = AbcBarLineKind.ThinThick;
        }
        else if (core == "[|")
        {
            kind = AbcBarLineKind.ThickThin;
        }
        else if (core == "||")
        {
            kind = AbcBarLineKind.ThinThin;
        }
        else
        {
            kind = AbcBarLineKind.Thin;
        }

        // "|::" plays its section three times, "|:::" four, and so on.
        int repeatCount = endsRepeat ? Math.Max(2, leadingDots + 1) : 1;
        return new AbcBarLine(kind, startsRepeat, endsRepeat, repeatCount, text);
    }

    private void CloseBar(AbcVoiceBuilder voice, AbcBarLine line)
    {
        if (voice.InChord)
        {
            _context.AddOnce("chord-unterminated", "A chord was left unterminated at a bar line; the notes in it were kept.");
            CloseChord(voice, new AbcToken(AbcTokenKind.ChordEnd));
        }

        if (voice.InGrace)
        {
            _context.AddOnce("grace-unterminated", "A grace group was left unterminated at a bar line; the notes in it were kept.");
            CloseGrace(voice);
        }

        if (voice.InTuplet)
        {
            _context.AddOnce("tuplet-across-bar", "A tuplet reached a bar line before it was finished; it was closed at the bar line.");
            CloseTuplet(voice);
        }

        if (voice.Current.Count == 0 && voice.PendingEndings.Count == 0)
        {
            // Two bar lines with nothing between them - which is what a line ending with '|' and
            // the next beginning with '|' produces - are one bar line, not an empty bar.
            voice.Opening = MergeBarLines(voice.Opening, line);
            return;
        }

        voice.Bars.Add(new AbcBar(voice.Current, voice.Opening, line, voice.PendingEndings));
        voice.Current.Clear();
        voice.PendingEndings.Clear();
        voice.Opening = line;
        voice.LastList = null;
        voice.LastIndex = -1;
    }

    private static AbcBarLine MergeBarLines(AbcBarLine existing, AbcBarLine added)
    {
        if (existing == null || existing.Kind == AbcBarLineKind.None && !existing.StartsRepeat && !existing.EndsRepeat)
        {
            return added;
        }

        return new AbcBarLine(
            added.Kind == AbcBarLineKind.None ? existing.Kind : added.Kind,
            existing.StartsRepeat || added.StartsRepeat,
            existing.EndsRepeat || added.EndsRepeat,
            Math.Max(existing.RepeatCount, added.RepeatCount),
            added.Text.Length > 0 ? added.Text : existing.Text);
    }

    // --- voices -------------------------------------------------------------------------------

    private AbcVoiceBuilder EnsureVoice(string id)
    {
        if (_voicesById.TryGetValue(id, out var existing))
        {
            return existing;
        }

        var voice = new AbcVoiceBuilder(id);
        _voices.Add(voice);
        _voicesById.Add(id, voice);
        if (_voices.Count == 1)
        {
            ApplyHeaderDirectives(voice);
        }

        return voice;
    }

    private AbcVoiceBuilder CurrentVoice()
    {
        if (_currentVoice == null)
        {
            _currentVoice = EnsureVoice(string.Empty);
        }

        return _currentVoice;
    }

    private void DeclareVoice(string value)
    {
        AbcFieldParser.ParseVoice(value, out string id, out string name);
        var voice = EnsureVoice(id);
        if (name.Length > 0)
        {
            voice.Name = name;
        }

        _currentVoice = voice;
    }

    private void SwitchVoice(string value)
    {
        AbcFieldParser.ParseVoice(value, out string id, out string name);
        var voice = EnsureVoice(id);
        if (name.Length > 0)
        {
            voice.Name = name;
        }

        _currentVoice = voice;
    }

    // --- directives ---------------------------------------------------------------------------

    private void ReadInstruction(string value)
    {
        // "I:MIDI program 73" and "%%MIDI program 73" are the same instruction written two ways.
        string text = (value ?? string.Empty).Trim();
        if (text.StartsWith("MIDI", StringComparison.OrdinalIgnoreCase))
        {
            ReadDirective(text);
            return;
        }

        _context.AddOnce("instructions", "I: instructions were skipped; they change how the music is printed, not what is played.");
    }

    private void ReadDirective(string text)
    {
        if (!text.StartsWith("MIDI", StringComparison.OrdinalIgnoreCase))
        {
            _context.AddOnce("directives", "Stylesheet directives were skipped; they change how the music is printed, not what is played.");
            return;
        }

        string rest = text.Substring(4).TrimStart();
        if (rest.StartsWith("=", StringComparison.Ordinal))
        {
            rest = rest.Substring(1).TrimStart();
        }

        string[] parts = rest.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            _context.AddOnce("midi-directives", "%%MIDI directives other than program and channel were skipped.");
            return;
        }

        if (string.Equals(parts[0], "program", StringComparison.OrdinalIgnoreCase))
        {
            ReadProgramDirective(parts);
            return;
        }

        if (string.Equals(parts[0], "channel", StringComparison.OrdinalIgnoreCase))
        {
            ReadChannelDirective(parts);
            return;
        }

        _context.AddOnce("midi-directives", "%%MIDI directives other than program and channel were skipped.");
    }

    // "%%MIDI program n" and "%%MIDI program c n", where n is 0 to 127 as abc2midi counts programs
    // and c is a channel from 1 to 16.
    private void ReadProgramDirective(string[] parts)
    {
        int channel = 0;
        int program;

        if (parts.Length >= 3 &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int first) &&
            int.TryParse(StripComment(parts[2]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int second))
        {
            channel = first;
            program = second;
        }
        else if (parts.Length >= 2 &&
            int.TryParse(StripComment(parts[1]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int only))
        {
            program = only;
        }
        else
        {
            _context.Add("A %%MIDI program directive carried no program number; it was ignored.");
            return;
        }

        if (program < 0 || program > 127)
        {
            _context.Add($"A %%MIDI program directive asked for program {program}, which is outside 0 to 127; it was ignored.");
            return;
        }

        if (channel != 0 && (channel < 1 || channel > 16))
        {
            _context.Add($"A %%MIDI program directive named channel {channel}, which is outside 1 to 16; the voice's own channel was used.");
            channel = 0;
        }

        if (_currentVoice == null)
        {
            _headerProgram = program;
            return;
        }

        if (!_currentVoice.MidiProgram.HasValue)
        {
            _currentVoice.MidiProgram = program;
        }

        TargetList(_currentVoice).Add(new AbcProgramChange(program, channel));
    }

    // "%%MIDI channel n", n from 1 to 16 as abc2midi counts channels.
    private void ReadChannelDirective(string[] parts)
    {
        if (parts.Length < 2 ||
            !int.TryParse(StripComment(parts[1]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int channel))
        {
            _context.Add("A %%MIDI channel directive carried no channel number; it was ignored.");
            return;
        }

        if (channel < 1 || channel > 16)
        {
            _context.Add($"A %%MIDI channel directive asked for channel {channel}, which is outside 1 to 16; it was ignored.");
            return;
        }

        if (_currentVoice == null)
        {
            _headerChannel = channel;
            return;
        }

        _currentVoice.MidiChannel = channel;
    }

    private static string StripComment(string value)
    {
        int hash = value.IndexOf('#');
        return hash >= 0 ? value.Substring(0, hash) : value;
    }

    private void ReportSkippedField(char field)
    {
        _context.AddOnce(
            "field-" + field,
            $"The {field}: field was skipped; it does not change what is played.");
    }

    // --- finishing ----------------------------------------------------------------------------

    private AbcDuration CurrentUnitNoteLength() =>
        _unitNoteLengthWasSet ? _unitNoteLength : AbcUnitNoteLength.DefaultFor(_meter);

    private AbcTune Build()
    {
        var voices = new List<AbcVoice>(_voices.Count);
        for (int i = 0; i < _voices.Count; i++)
        {
            var voice = _voices[i];

            if (voice.InChord)
            {
                _context.AddOnce("chord-unterminated", "A chord was left unterminated at the end of the tune; the notes in it were kept.");
                CloseChord(voice, new AbcToken(AbcTokenKind.ChordEnd));
            }

            if (voice.InGrace)
            {
                _context.AddOnce("grace-unterminated", "A grace group was left unterminated at the end of the tune; the notes in it were kept.");
                CloseGrace(voice);
            }

            if (voice.InTuplet)
            {
                _context.AddOnce("tuplet-unterminated", "A tuplet was left unfinished at the end of the tune; it was closed there.");
                CloseTuplet(voice);
            }

            if (voice.Current.Count > 0 || voice.PendingEndings.Count > 0)
            {
                voice.Bars.Add(new AbcBar(voice.Current, voice.Opening, AbcBarLine.None, voice.PendingEndings));
                voice.Current.Clear();
                voice.PendingEndings.Clear();
            }

            voices.Add(new AbcVoice(voice.Id, voice.Name, voice.MidiProgram, voice.MidiChannel, voice.Bars));
        }

        return new AbcTune(
            _referenceNumber,
            _titles,
            _composer,
            _startMeter,
            new AbcUnitNoteLength(_startUnitNoteLength, !_startUnitNoteLengthWasSet),
            _tempo,
            _startKey,
            voices,
            _context.Problems);
    }

    // --- lines --------------------------------------------------------------------------------

    // An information field is a letter followed immediately by a colon at the start of a line. In
    // the tune BODY the standard forbids the identifiers that would be confused with notes, rests
    // and spacers - A-G, a-g, X-Z and x-z - so those lines are music code, not fields.
    internal static bool TryReadFieldLine(string line, out char field, out string value)
    {
        field = '\0';
        value = string.Empty;

        if (line.Length < 2 || line[1] != ':')
        {
            return false;
        }

        char letter = line[0];
        if (!char.IsLetter(letter))
        {
            return false;
        }

        field = letter;
        value = line.Substring(2).Trim();
        return true;
    }

    internal static bool IsLegalBodyField(char field)
    {
        switch (field)
        {
            case 'I':
            case 'K':
            case 'L':
            case 'M':
            case 'm':
            case 'N':
            case 'P':
            case 'Q':
            case 'r':
            case 'R':
            case 's':
            case 'T':
            case 'U':
            case 'V':
            case 'W':
            case 'w':
                return true;
            default:
                return false;
        }
    }
}
