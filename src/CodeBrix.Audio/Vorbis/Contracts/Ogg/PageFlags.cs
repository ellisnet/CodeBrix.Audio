using System;

namespace CodeBrix.Audio.Vorbis.Contracts.Ogg; //was previously: NVorbis.Contracts.Ogg;

[Flags]
enum PageFlags
{
    None = 0,
    ContinuesPacket = 1,
    BeginningOfStream = 2,
    EndOfStream = 4,
}
