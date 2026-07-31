using System;

namespace CodeBrix.Audio.Vorbis.Contracts.Ogg; //was previously: NVorbis.Contracts.Ogg;

interface IPacketReader
{
    Memory<byte> GetPacketData(int pagePacketIndex);

    void InvalidatePacketCache(IPacket packet);
}
