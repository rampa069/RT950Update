namespace RT950Update.Core;

public class PackageFmt
{
    public byte Command { get; set; }
    public byte CommandArgs { get; set; }
    public bool Verify { get; set; }

    public byte[] Packing(byte cmd, ushort cmdArgs, ushort dataLen, byte[]? data)
    {
        byte[] array = new byte[6 + dataLen + 2 + 1];
        array[0] = 0xAA; // PACKAGE_HEADER
        array[1] = cmd;
        array[2] = (byte)(cmdArgs >> 8);
        array[3] = (byte)cmdArgs;
        array[4] = (byte)(dataLen >> 8);
        array[5] = (byte)dataLen;

        if (data != null)
        {
            for (int i = 0; i < dataLen; i++)
            {
                array[i + 6] = data[i];
            }
        }

        int crc = CrcValidation(array, 1, 5 + dataLen);
        array[6 + dataLen] = (byte)(crc >> 8);
        array[6 + dataLen + 1] = (byte)crc;
        array[6 + dataLen + 2] = 0x55; // PACKAGE_END

        return array;
    }

    public byte[] AnalysePackage(byte[] package)
    {
        Command = package[1];
        CommandArgs = package[2];
        CommandArgs <<= 8;
        CommandArgs |= package[3];

        int dataLen = package[4];
        dataLen <<= 8;
        dataLen |= package[5];

        byte[] array = new byte[dataLen];
        for (int i = 0; i < dataLen; i++)
        {
            array[i] = package[6 + i];
        }

        int calculatedCrc = CrcValidation(package, 1, 5 + dataLen);
        int receivedCrc = package[6 + dataLen];
        receivedCrc <<= 8;
        receivedCrc |= package[6 + dataLen + 1];

        calculatedCrc &= 0xFFFF;
        receivedCrc &= 0xFFFF;

        Verify = (calculatedCrc == receivedCrc);

        return array;
    }

    private int CrcValidation(byte[] dat, int offset, int count)
    {
        int crc = 0;
        for (int i = 0; i < count; i++)
        {
            int data = dat[i + offset];
            crc ^= data << 8;
            for (int j = 0; j < 8; j++)
            {
                crc = ((crc & 0x8000) != 0x8000)
                    ? (crc << 1)
                    : ((crc << 1) ^ 0x1021);
            }
        }
        return crc;
    }
}
