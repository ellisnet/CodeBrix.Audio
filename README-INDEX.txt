================================================================================
README-INDEX: CodeBrix.Audio
Map of the README files in this repository
================================================================================

If you are an AI coding agent: find the NuGet package you are consuming below
and read its AGENT-README file in full. Read MAINTAINER-README.txt only if you
are changing this repository itself.

AGENT-README FILES (consumer documentation, one per NuGet package)
------------------------------------------------------------------
  AGENT-README.txt
      CodeBrix.Audio.MitLicenseForever - fully managed, cross-platform audio
      file library (WAV / MP3 / Ogg Vorbis / FLAC, MIDI, ID3 and Vorbis tags,
      SoundFont and SFZ synthesis, DSP primitives) plus the bundled
      CodeBrix.Audio.Engine assembly for device playback and recording. One
      file covers both assemblies, because one package ships both.

MAINTAINER AND EXTRAS
---------------------
  MAINTAINER-README.txt
      Building, testing, packaging, versioning and provenance notes for
      maintainers.
  EXTRAS-README.txt
      Samples, tools and other non-package content in this repository.

GENERAL
-------
  README.md
      Human-facing overview shown on GitHub and nuget.org.
  README-INDEX.txt
      This file.
  THIRD-PARTY-NOTICES.txt
      What came from where, and under which licences. Packed into the nupkg, and
      the authoritative provenance record for both bundled assemblies and the
      native backend.

ALSO WORTH READING, IN PLACE
----------------------------
  tools/build_native_libraries/README.txt
      How the seven native backends are built and verified, why the Linux ones
      are built in containers, and the steps for adopting a built binary into
      the package - including the LICENSE-MiniAudio.txt that must travel with
      it. Read it before touching anything native.
================================================================================
