namespace CodeBrix.Audio.Vorbis.Contracts.Ogg; //was previously: NVorbis.Contracts.Ogg;

interface ICrc
{
    void Reset();
    void Update(int nextVal);
    bool Test(uint checkCrc);
}
