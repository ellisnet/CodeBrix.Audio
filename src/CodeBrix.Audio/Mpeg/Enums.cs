using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CodeBrix.Audio.Mpeg; //was previously: NLayer;

internal enum MpegVersion
{
    Unknown = 0,
    Version1 = 10,
    Version2 = 20,
    Version25 = 25,
}

internal enum MpegLayer
{
    Unknown = 0,
    LayerI = 1,
    LayerII = 2,
    LayerIII = 3,
}

internal enum MpegChannelMode
{
    Stereo,
    JointStereo,
    DualChannel,
    Mono,
}

internal enum StereoMode
{
    Both,
    LeftOnly,
    RightOnly,
    DownmixToMono,
}
