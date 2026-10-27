using NTRModern;

namespace LibRecomp.Tests;

public class LoadsTests
{
    public LoadsTests() => Hardware.Reset(Model.DS);

    [Fact]
    public void MisalignedWordsRotateOnBothCPUs()
    {
        NTRModern.ARM9.Memory.Write32(0x02000200, 0x11223344);

        Assert.Equal(0x44112233u, ARM9.Loads.Word(0x02000201));
        Assert.Equal(0x44112233u, ARM7.Loads.Word(0x02000201));
    }

    [Fact]
    public void TheARM9ReadsOddHalfwordsFromTheEvenAddressBelow()
    {
        NTRModern.ARM9.Memory.Write16(0x02000200, 0x80F0);

        Assert.Equal(0x000080F0u, ARM9.Loads.Halfword(0x02000201));
        Assert.Equal(0xFFFF80F0u, ARM9.Loads.SignedHalfword(0x02000201));
    }

    [Fact]
    public void TheARM7RotatesOddHalfwordsAndReadsASignedByteForOddSignedOnes()
    {
        NTRModern.ARM7.Memory.Write16(0x02000200, 0x80F0);

        Assert.Equal(0xF0000080u, ARM7.Loads.Halfword(0x02000201));
        Assert.Equal(0xFFFFFF80u, ARM7.Loads.SignedHalfword(0x02000201));
        Assert.Equal(0xFFFF80F0u, ARM7.Loads.SignedHalfword(0x02000200));
    }
}
