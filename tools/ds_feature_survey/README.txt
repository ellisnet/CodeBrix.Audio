==============================================================================================
ds_feature_survey - measure which Decent Sampler features real libraries actually use
==============================================================================================

WHAT THIS IS
  The Decent Sampler counterpart of sfz_opcode_survey: a tool for checking the scope of format
  support by COUNTING rather than guessing.

  The developer guide tells you what the format CAN say. It does not tell you what the libraries
  you actually own say, and it does not tell you whether this engine understands them. This
  parses a folder of real libraries with CodeBrix.Audio's own parser and reports, per library and
  per preset, every element and attribute they use, plus how much of that the engine recognises.

  Keep it after the Decent Sampler work ships. Re-running it over a new library says immediately
  whether that library will load cleanly, and exactly which names it uses that the engine has
  never seen.

  Unlike its shell-script siblings in tools/, this one needs the parser, so it is a small console
  project referencing the library. It is not packable and is not in CodeBrix.Audio.slnx.

USAGE
  cd tools/ds_feature_survey
  dotnet run -- <corpus-directory> [output-directory]

  Each IMMEDIATE SUBDIRECTORY of <corpus-directory> is treated as one library. Every .dspreset,
  .dslibrary and .dsbundle under it is read recursively; archives are read in place, without
  being unpacked.

      corpus/
        2114_HimalayanVibes_.../       <- one library
        Global Swarm (DS)/             <- one library
        The Spellsinger/

PREREQUISITES (installed by YOU - this tool never installs anything)
  The .NET 10 SDK, and a corpus of Decent Sampler libraries you supply. Nothing is downloaded.

WHAT IT WRITES
  libraries.md  Per-preset breakdown: library, preset, groups, samples, oscillators, file size,
                and the number of problems. Every problem is then listed in full, per preset.
                This is where authoring mistakes in a library show up - a damping written with a
                letter O instead of a zero, a binding level of "groups" that does not exist.

  attributes.md Every element, attribute, effect type, binding type, binding level, binding
                parameter, oscillator waveform, modulator and sample extension found, each with
                the number of libraries, presets and raw uses, and an example value.

  coverage.md   Coverage against DecentSamplerSupportedFeatures, which the tool reads out of the
                library assembly it builds against, so it stays truthful as the engine grows.
                Says how many presets parse with nothing unrecognised, and names anything the
                engine does not know. This is the report that says whether the shipping scope met
                its target, and the one to re-run over a new library before promising it will
                play.

THE COUNTING RULE THAT MATTERS
  Names are ranked by the number of LIBRARIES that use them, never by raw occurrence count. One
  sprawling library must not decide the ranking: Global Swarm alone has 464 samples and would
  drown out eight other libraries. Raw occurrences are reported, never ranked on.

  Controller-indexed attributes are folded to one name: loCC64 and loCC11 both count once, as
  loCCN. That is the unit somebody actually implements. The folding lives in
  DecentSamplerSupportedFeatures.CanonicalAttributeName, shared with the engine, so the survey and
  the feature list can never disagree about names.

ATTRIBUTES ARE COUNTED FROM THE RAW XML
  The parsed model keeps only the attributes the parser did NOT understand. Counting from that
  would make the coverage figure a restatement of what the engine already knows. So the tool reads
  each preset twice - once with the parser, once as plain XML - and counts attribute usage from
  the plain XML. If you change that, the coverage number stops meaning anything.

WHAT A HEALTHY RUN LOOKS LIKE
  Zero parse errors, zero unrecognised attributes, and a handful of problems that are genuine
  authoring mistakes in the libraries themselves. A preset with many problems and few samples is
  usually a preset whose sample folder is missing, not a preset the engine mishandled - check
  libraries.md before believing any number.

BUILDING A CORPUS
  Aim for spread rather than volume. Pianobook is the obvious source of free libraries: choir and
  vocal libraries stress round robins and release triggers; pianos stress deep velocity layers and
  the sustain pedal; texture libraries stress loops, crossfades and instrument-level effects.
  Include at least one .dslibrary archive, because in-place archive reading is a separate path.
==============================================================================================
