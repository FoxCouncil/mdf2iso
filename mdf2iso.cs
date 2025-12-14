using System.Text;

internal class Mdf2Iso
{
    private const string VERSION = "1.0.0";

    private static readonly byte[] SYNC_HEADER = {
        0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00
    };

    private static readonly byte[] SYNC_HEADER_MDF_AUDIO = {
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0x80, 0xC0, 0x80, 0x80, 0x80, 0x80
    };

    private static readonly byte[] SYNC_HEADER_MDF = {
        0x80, 0xC0, 0x80, 0x80, 0x80, 0x80,
        0x80, 0xC0, 0x80, 0x80, 0x80, 0x80
    };

    private static readonly byte[] ISO_9660 = {
        0x01, 0x43, 0x44, 0x30, 0x30, 0x31, 0x01, 0x00
    };

    private static void Usage()
    {
        Console.WriteLine($"mdf2iso v{VERSION}");
        Console.WriteLine("Usage:");
        Console.WriteLine("mdf2iso [OPTION] [BASENAME.MDF] [DESTINATION]");
        Console.WriteLine();
        Console.WriteLine("--toc    Generate toc file");
        Console.WriteLine("--cue    Generate cue file");
        Console.WriteLine("--help   Display this notice");
    }

    private static void MainPercent(long percent)
    {
        int bars = (int)(percent / 5);
        Console.Write($"{percent}% [:");
        for (int i = 0; i < bars; i++)
        {
            Console.Write("=");
        }

        Console.Write(">");
        for (int i = bars; i < 20; i++)
        {
            Console.Write(" ");
        }

        Console.Write(":]\r");
    }

    private static void CueSheet(string dest)
    {
        string cue = Path.ChangeExtension(dest, ".cue");
        string bin = Path.ChangeExtension(dest, ".bin");

        string binStr = bin.Replace(".\\", "");

        using StreamWriter sw = new(cue, false, Encoding.ASCII);
        sw.WriteLine($"FILE \"{binStr}\" BINARY");
        sw.WriteLine("  TRACK 01 MODE2/2352");
        sw.WriteLine("    INDEX 01 00:00:00");

        /* FILE "{FILE}" BINARY
         *   TRACK 01 MODE2/2352
         *     INDEX 01 00:00:00
         * */

        File.Move(dest, bin, true);

        Console.WriteLine($"Created Cuesheet : {cue}");
    }

    private static void TocFile(string dest, bool sub)
    {
        string toc = Path.ChangeExtension(dest, ".toc");
        string dat = Path.ChangeExtension(dest, ".dat");

        string datStr = dat.Replace(".\\", "");

        using StreamWriter sw = new(toc, false, Encoding.ASCII);
        sw.WriteLine("CD_ROM");
        sw.WriteLine("// Track 1");
        sw.Write("TRACK MODE1_RAW");
        sw.WriteLine(sub ? " RW_RAW" : "");
        sw.WriteLine("NO COPY");
        sw.WriteLine($"DATAFILE \"{datStr}\"");

        File.Move(dest, dat, true);

        Console.WriteLine($"Created TOC File : {toc}");
    }

    private static bool MemEq(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    private static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Usage();
            return;
        }

        bool cue = false;
        bool toc = false;

        foreach (var a in args)
        {
            if (a == "--help") { Usage(); return; }
            if (a == "--cue")
            {
                cue = true;
            }

            if (a == "--toc")
            {
                toc = true;
            }
        }

        if (cue && toc)
        {
            Usage();
            return;
        }

        int optCount = (cue ? 1 : 0) + (toc ? 1 : 0);
        if (args.Length < 1 + optCount)
        {
            Usage();
            return;
        }

        string src = args[optCount];
        string dest = args.Length > optCount + 1
            ? args[optCount + 1]
            : Path.ChangeExtension(src, ".iso");

        using FileStream fs = new(src, FileMode.Open, FileAccess.Read);
        using BinaryReader br = new(fs);

        _ = fs.Seek(32768, SeekOrigin.Begin);
        byte[] test = br.ReadBytes(8);

        if (MemEq(test, ISO_9660))
        {
            Console.WriteLine("This is file iso9660 ;)");
            return;
        }

        _ = fs.Seek(0, SeekOrigin.Begin);
        byte[] hdr = br.ReadBytes(12);

        int seekHead, seekEcc, sectorSize, sectorData;
        bool subToc = false;

        if (MemEq(hdr, SYNC_HEADER))
        {
            _ = fs.Seek(2352, SeekOrigin.Begin);
            hdr = br.ReadBytes(12);

            if (MemEq(hdr, SYNC_HEADER_MDF))
            {
                if (cue)
                {
                    seekEcc = 96;
                    sectorSize = 2448;
                    sectorData = 2352;
                    seekHead = 0;
                }
                else if (!toc)
                {
                    seekEcc = 384;
                    sectorSize = 2448;
                    sectorData = 2048;
                    seekHead = 16;
                }
                else
                {
                    seekEcc = 0;
                    sectorSize = 2448;
                    sectorData = 2448;
                    seekHead = 0;
                    subToc = true;
                }
            }
            else
            {
                if (cue)
                {
                    seekEcc = 0;
                    sectorSize = 2352;
                    sectorData = 2352;
                    seekHead = 0;
                }
                else if (!toc)
                {
                    seekEcc = 288;
                    sectorSize = 2352;
                    sectorData = 2048;
                    seekHead = 16;
                }
                else
                {
                    seekEcc = 0;
                    sectorSize = 2352;
                    sectorData = 2352;
                    seekHead = 0;
                }
            }
        }
        else
        {
            _ = fs.Seek(2352, SeekOrigin.Begin);
            hdr = br.ReadBytes(12);
            if (!MemEq(hdr, SYNC_HEADER_MDF_AUDIO))
            {
                throw new Exception("Unknown format");
            }

            seekHead = 0;
            sectorSize = 2448;
            seekEcc = 96;
            sectorData = 2352;
            cue = false;
        }

        using FileStream fd = new(dest, FileMode.Create, FileAccess.Write);
        using BinaryWriter bw = new(fd);

        long totalSectors = fs.Length / sectorSize;
        long totalBytes = totalSectors * sectorData;

        _ = fs.Seek(0, SeekOrigin.Begin);
        byte[] buf = new byte[sectorData];

        long lastPercent = -1;

        for (long i = 0; i < totalSectors; i++)
        {
            _ = fs.Seek(seekHead, SeekOrigin.Current);
            _ = br.Read(buf, 0, sectorData);
            bw.Write(buf, 0, sectorData);
            _ = fs.Seek(seekEcc, SeekOrigin.Current);

            long percent = i * sectorData * 100 / totalBytes;
            if (percent != lastPercent)
            {
                MainPercent(percent);
                lastPercent = percent;
            }
        }

        Console.WriteLine("100%[:====================:]");

        bw.Close();
        fd.Close();

        if (cue)
        {
            CueSheet(dest);
        }
        else if (toc)
        {
            TocFile(dest, subToc);
        }
        else
        {
            Console.WriteLine($"Create iso9660: {dest}");
        }
    }
}
