==============================================================================================
sfz_opcode_survey - measure which SFZ opcodes real libraries actually use
==============================================================================================

WHAT THIS IS
  A tool for deciding the scope of SFZ support by COUNTING rather than guessing.

  Neither the SFZ specification nor any player's support matrix tells you what YOUR libraries
  need. This parses a folder of real SFZ libraries with CodeBrix.Audio's own parser and reports
  which opcodes they use, so "near-full SFZ support requires {this list}" becomes a measured,
  checkable statement instead of an opinion.

  Keep it after the SFZ work ships. Re-running it over a new library says immediately whether
  that library will play, and which opcodes it would need.

  Unlike its shell-script siblings in tools/, this one needs the SFZ parser, so it is a small
  console project referencing the library. It is not packable and is not in CodeBrix.Audio.slnx.

USAGE
  cd tools/sfz_opcode_survey
  dotnet run -- <corpus-directory> [output-directory]

  Each IMMEDIATE SUBDIRECTORY of <corpus-directory> is treated as one library. Every .sfz file
  under it is parsed recursively, following #include.

      corpus/
        salamander-grand-piano/     <- one library
        virtuosity_drums/           <- one library
        sonatina-symphonic-orchestra/

PREREQUISITES (installed by YOU - this tool never installs anything)
  The .NET 10 SDK, and a corpus of SFZ libraries you supply. Nothing is downloaded.

WHAT IT WRITES
  opcodes.md    Every opcode found, ranked by how many LIBRARIES use it, with raw occurrence
                counts alongside.
  coverage.md   The coverage curve: for each N, how many libraries would load with ZERO
                unimplemented opcodes if the top N opcodes were implemented. Plus, for the top
                50, exactly which opcodes each library still needs.
  libraries.md  Per-library breakdown: files, regions, distinct opcodes, parse problems.

THE COUNTING RULE THAT MATTERS
  Opcodes are ranked by the number of LIBRARIES that use them, never by raw occurrence count.
  An opcode used once in a library counts the same as one used ten thousand times. Without that
  rule a single sprawling library decides the entire ranking - Sonatina alone has 40,000 regions
  and would drown out fifteen other libraries. Raw occurrences are reported, never ranked on.

  Indexed opcodes are folded to one name: volume_oncc11 and volume_oncc74 both count once, as
  volume_onccN. That is the unit somebody actually implements - _oncc is a mechanism, not one
  opcode per CC number.

ONLY ROOT FILES ARE COUNTED
  A .sfz that some other file #includes is NOT counted on its own. Only "root" files - the ones
  nothing else includes - contribute opcodes.

  This is not tidiness, it is correctness. A file meant to be included is written assuming the
  root file's #define variables are in scope. Parsed standalone it keeps its $variables, so
  amplitude_oncc$ch_hh and amp_velcurve_$v11h look like brand new opcodes, one per variable. On
  this corpus that inflated the distinct-opcode count from 233 to 529 - 56% pure noise - and it
  inflated the "opcodes needed for full support" figure by roughly seven times. It also double
  counts every included file's opcodes.

  If a run reports an implausibly large number of distinct opcodes, or opcode names containing
  '$', this is the first thing to check.

BUILDING A CORPUS - THE TRAP TO AVOID
  Many libraries put their real content in .txt or .inc files and use the .sfz file only as a
  shell of #include lines. A corpus assembled by copying *.sfz alone will show those libraries
  as having zero regions and zero opcodes, and the survey will silently under-report.

  Check libraries.md after every run. A library with many "parse problems" and few regions has
  unresolved includes, not simple content. Fix the corpus and re-run before believing any number.

  Aim for genre spread rather than volume: drum kits stress round robins, off groups and
  one-shots; pianos stress deep velocity layers, release samples and the sustain pedal;
  orchestral libraries stress key switches and articulation layers.
==============================================================================================
