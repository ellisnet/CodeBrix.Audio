# SunoInstrumentSwap

A small console application that renders a stems export twice: once as it was downloaded, and
again with every part that has a usable transcription played through an **instrument library**
instead — the vocals left as the recording, and any part you name given an instrument of its own.

It is the one-line way from the manual, made runnable.

## What it shows

* `SunoPlayerOptions.InstrumentLibraryName` — changing what a whole song sounds like is a name.
* `SunoStemSelection.EverythingBut(...)` — which parts play from their transcription, with a floor
  on the note count and on the coverage, instead of a loop over every track.
* `SunoPlayerOptions.StemInstruments` — one named part played through one instrument file.
* `MultiTrackPlayer.Problems` — a part the library cannot cover, reported rather than thrown.
* `MeasureRelativeTrackLevels(rate)` offline, with no audio device, and `LastLevelMatch` saying
  what the matched mix will peak at and what `Volume` would make it fit.
* `RenderToFile(path, WaveFormat)` — a 16-bit bounce in one argument.
* `SunoStem.UsedNotes` / `LowestNote` / `HighestNote` — what a transcription actually plays.

## Running it

```
SunoInstrumentSwap <stems zip or folder> --output <folder> [options]
```

`--output` is required and must be **outside this repository**: a render is hundreds of megabytes
and so is the extraction cache. The application refuses a path inside the repository rather than
writing there.

```
--library <name>               an instrument library to render with, by its registered name;
                               repeatable. Defaults to ModestSynthGm.
--soundfont <name>=<path>      register a .sf2 as a library under <name> and render with it too.
--stem-instrument <stem>=<path>
                               play ONE named stem with one instrument file - a .dspreset,
                               .dslibrary, .dsbundle, a Decent Sampler library folder, a .sfz or
                               a .sf2.
--vocal-stems <a,b>            which stems keep the download's audio whatever their transcription
                               says. Default: Vocals,Backing Vocals.
--min-notes <n>                the note-count floor a transcription must clear. Default 12.
--min-coverage <x>             the coverage floor a transcription must clear. Default 0.02.
--rate <hz>                    the rate every render is produced at. Default 48000, because a
                               stems export is 48 kHz throughout.
--float                        write 32-bit float instead of 16-bit PCM.
--cache <folder>               where a zip is extracted. Default: a folder beside the renders.
--help                         the same text, from the application itself.
```

An example, with two libraries, a SoundFont of your own and one part re-voiced:

```
SunoInstrumentSwap "~/Downloads/My Song Stems.zip" --output ~/renders \
    --library ModestSynthGm \
    --library FluidR3Gm \
    --soundfont MyBank=~/SoundFonts/bank.sf2 \
    --stem-instrument "Synth=~/packs/Grand Piano/Grand Piano.dspreset"
```

It prints, per stem: whether it played from the recording or the transcription, which instrument
it used, the alignment offset, the matched gain, and anything that could not be honoured.

## What it needs

* The .NET SDK, and this repository — it references `src/CodeBrix.Audio` and
  `src/CodeBrix.Audio.ModestSynth` as projects, so it shows the current API rather than the last
  published one.
* Nothing else to make a sound: ModestSynthGm is synthesized and has no files to find.
* `CodeBrix.Audio.Samples.FluidR3Gm.MitLicenseForever` from nuget.org, for the sampled General
  MIDI bank. It brings a large SoundFont into the output folder and registers itself as
  `FluidR3Gm`; the application registers it only when that file is really there, so the sample
  still runs if it is not.
* A Suno stems download of your own, and somewhere outside this repository to write to.

Nothing about any particular song is in this sample. Everything it knows about a song it reads
from the file you point it at.

## Building it

```
dotnet build SunoInstrumentSwap.slnx -c Release
```

This sample has its own solution and is deliberately **not** part of `CodeBrix.Audio.slnx`: it is
not built, tested or published with the library. It is never packed and nothing here reaches
NuGet.
