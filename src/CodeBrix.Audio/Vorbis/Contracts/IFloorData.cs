namespace CodeBrix.Audio.Vorbis.Contracts; //was previously: NVorbis.Contracts;

interface IFloorData
{
    bool ExecuteChannel { get; }
    bool ForceEnergy { get; set; }
    bool ForceNoEnergy { get; set; }
}
