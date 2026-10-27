namespace LibRecomp;

internal static partial class BIOS
{
    private static void BitUnPack(CPU cpu, RecompContext ctx, uint source, uint destination, uint info)
    {
        int length = cpu.Read16(info);
        int sourceWidth = cpu.Read8(info + 2);
        int destinationWidth = cpu.Read8(info + 3);
        uint offset = cpu.Read32(info + 4);
        bool offsetZeros = (offset & 0x80000000) != 0;
        offset &= 0x7FFFFFFF;

        uint sourceMask = (1u << sourceWidth) - 1;
        uint destinationMask = destinationWidth == 32 ? uint.MaxValue : (1u << destinationWidth) - 1;
        uint word = 0;
        int bits = 0;

        for (int i = 0; i < length; i++)
        {
            byte value = cpu.Read8(source + (uint)i);
            for (int shift = 0; shift < 8; shift += sourceWidth)
            {
                uint unit = (uint)(value >> shift) & sourceMask;
                if (unit != 0 || offsetZeros)
                {
                    unit += offset;
                }

                word |= (unit & destinationMask) << bits;
                bits += destinationWidth;
                if (bits == 32)
                {
                    cpu.Write32(destination, word);
                    destination += 4;
                    word = 0;
                    bits = 0;
                    CheckEvents(cpu, ctx);
                }
            }
        }
    }

    private static void LZ77UnComp(Source source, Output output)
    {
        if (!source.Open(output.Address, out int remaining))
        {
            return;
        }

        while (remaining > 0)
        {
            byte flags = source.Read8();
            for (int block = 0; block < 8 && remaining > 0; block++, flags <<= 1)
            {
                if ((flags & 0x80) == 0)
                {
                    output.Write(source.Read8());
                    remaining--;
                    continue;
                }

                byte first = source.Read8();
                byte second = source.Read8();
                int count = (first >> 4) + 3;
                uint distance = (uint)(((first & 0xF) << 8) | second) + 1;
                for (; count > 0 && remaining > 0; count--, remaining--)
                {
                    output.Write(output.ReadBack(distance));
                }
            }
        }

        source.Close();
    }

    private static void HuffUnComp(CPU cpu, RecompContext ctx, Source source, uint destination)
    {
        if (!source.Open(destination, out int size))
        {
            return;
        }

        int dataBits = (int)(source.Header & 0xF);
        byte[] tree = new byte[(source.Read8() + 1) * 2];
        for (int i = 1; i < tree.Length; i++)
        {
            tree[i] = source.Read8();
        }

        const int Root = 1;
        int node = Root;
        uint word = 0;
        int wordBits = 0;
        uint written = 0;

        while (written < size)
        {
            uint bits = source.Read32();
            for (int bit = 31; bit >= 0 && written < size; bit--)
            {
                byte value = tree[node];
                int direction = (int)((bits >> bit) & 1);
                int child = (node & ~1) + ((value & 0x3F) * 2) + 2 + direction;
                bool isData = (value & (direction == 0 ? 0x80 : 0x40)) != 0;
                if (!isData)
                {
                    node = child;
                    continue;
                }

                word |= (uint)tree[child] << wordBits;
                wordBits += dataBits;
                node = Root;
                if (wordBits == 32)
                {
                    cpu.Write32(destination + written, word);
                    written += 4;
                    word = 0;
                    wordBits = 0;
                    CheckEvents(cpu, ctx);
                }
            }
        }

        source.Close();
    }

    private static void RLUnComp(Source source, Output output)
    {
        if (!source.Open(output.Address, out int remaining))
        {
            return;
        }

        while (remaining > 0)
        {
            byte flag = source.Read8();
            if ((flag & 0x80) != 0)
            {
                byte value = source.Read8();
                for (int count = (flag & 0x7F) + 3; count > 0 && remaining > 0; count--, remaining--)
                {
                    output.Write(value);
                }
            }
            else
            {
                for (int count = (flag & 0x7F) + 1; count > 0 && remaining > 0; count--, remaining--)
                {
                    output.Write(source.Read8());
                }
            }
        }

        source.Close();
    }

    private sealed class Source(CPU cpu, RecompContext ctx, uint address, uint? callbacks = null, uint parameter = 0)
    {
        private const uint OpenAndGet32 = 0;
        private const uint CloseFunction = 4;
        private const uint Get8 = 8;
        private const uint Get32 = 16;

        private uint _address = address;

        public uint Header { get; private set; }

        public bool Open(uint destination, out int size)
        {
            if (callbacks is not { } structure)
            {
                Header = cpu.Read32(_address);
            }
            else
            {
                Header = Call(structure + OpenAndGet32, _address, destination, parameter);
                if ((int)Header < 0)
                {
                    size = 0;
                    return false;
                }
            }

            _address += 4;
            size = (int)(Header >> 8);
            return true;
        }

        public byte Read8()
        {
            byte value = callbacks is { } structure ? (byte)Call(structure + Get8, _address) : cpu.Read8(_address);
            _address++;
            return value;
        }

        public uint Read32()
        {
            uint value = callbacks is { } structure ? Call(structure + Get32, _address) : cpu.Read32(_address);
            _address += 4;
            return value;
        }

        public void Close()
        {
            if (callbacks is not { } structure)
            {
                return;
            }

            uint size = Header >> 8;
            if (cpu.Read32(structure + CloseFunction) == 0)
            {
                ctx.R0 = size;
                return;
            }

            uint result = Call(structure + CloseFunction, _address);
            ctx.R0 = (int)result < 0 ? result : size;
        }

        private uint Call(uint pointer, uint r0, uint r1 = 0, uint r2 = 0)
        {
            (ctx.R0, ctx.R1, ctx.R2, ctx.R14) = (r0, r1, r2, SWIVector(cpu));
            cpu.Functions.Lookup(cpu.Read32(pointer))(ctx);
            cpu.Functions.ThrowIfUnwinding();
            return ctx.R0;
        }
    }

    private sealed class Output(CPU cpu, RecompContext ctx, uint address, bool halfwords)
    {
        private int _pending = -1;

        public uint Address { get; private set; } = address;

        public byte ReadBack(uint distance) => cpu.Read8(Address - distance);

        public void Write(byte value)
        {
            if (!halfwords)
            {
                cpu.Write8(Address++, value);
                CheckEvents(cpu, ctx);
                return;
            }

            if (_pending < 0)
            {
                _pending = value;
            }
            else
            {
                cpu.Write16(Address - 1, (ushort)(_pending | (value << 8)));
                _pending = -1;
                CheckEvents(cpu, ctx);
            }

            Address++;
        }
    }
}
