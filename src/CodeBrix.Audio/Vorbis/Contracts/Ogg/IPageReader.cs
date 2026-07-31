using System;

namespace CodeBrix.Audio.Vorbis.Contracts.Ogg; //was previously: NVorbis.Contracts.Ogg;

interface IPageReader : IDisposable
{
    void Lock();
    bool Release();

    long ContainerBits { get; }
    long WasteBits { get; }

    bool ReadNextPage();

    bool ReadPageAt(long offset);
}
