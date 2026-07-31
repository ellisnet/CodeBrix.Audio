namespace CodeBrix.Audio.Vorbis.Contracts.Ogg; //was previously: NVorbis.Contracts.Ogg;

interface IForwardOnlyPacketProvider : IPacketProvider
{
    bool AddPage(byte[] buf, bool isResync);
    void SetEndOfStream();
}
