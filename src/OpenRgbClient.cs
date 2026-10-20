using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace RGBUnify
{
    // Minimal client for the OpenRGB SDK network protocol (TCP, default port 6742).
    public class OpenRgbClient : IDisposable
    {
        const uint PKT_CONTROLLER_COUNT = 0;
        const uint PKT_CONTROLLER_DATA = 1;
        const uint PKT_PROTOCOL_VERSION = 40;
        const uint PKT_SET_CLIENT_NAME = 50;
        const uint PKT_DEVICE_LIST_UPDATED = 100;
        const uint PKT_RESIZE_ZONE = 1000;
        const uint PKT_UPDATE_LEDS = 1050;
        const uint PKT_SET_CUSTOM_MODE = 1100;
        const uint PKT_UPDATE_MODE = 1101;

        const uint CLIENT_PROTOCOL = 4;

        TcpClient tcp;
        NetworkStream stream;
        readonly object sync = new object();
        public uint Protocol { get; private set; }
        public volatile bool DeviceListChanged;
        public DateTime LastListUpdate = DateTime.MinValue;

        public bool Connected
        {
            get { return tcp != null && tcp.Connected; }
        }

        public bool Connect(string host, int port, int timeoutMs)
        {
            try
            {
                tcp = new TcpClient();
                tcp.NoDelay = true;
                IAsyncResult ar = tcp.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs) || !tcp.Connected)
                {
                    tcp.Close();
                    tcp = null;
                    return false;
                }
                tcp.EndConnect(ar);
                stream = tcp.GetStream();
                stream.ReadTimeout = 15000;

                byte[] name = Encoding.ASCII.GetBytes("RGB Unify\0");
                Send(0, PKT_SET_CLIENT_NAME, name);

                Send(0, PKT_PROTOCOL_VERSION, BitConverter.GetBytes(CLIENT_PROTOCOL));
                byte[] resp = WaitFor(PKT_PROTOCOL_VERSION, 3000);
                uint server = resp != null && resp.Length >= 4 ? BitConverter.ToUInt32(resp, 0) : 0;
                Protocol = Math.Min(server, CLIENT_PROTOCOL);
                return true;
            }
            catch
            {
                Dispose();
                return false;
            }
        }

        public void Dispose()
        {
            try { if (stream != null) stream.Close(); } catch { }
            try { if (tcp != null) tcp.Close(); } catch { }
            stream = null;
            tcp = null;
        }

        void Send(uint dev, uint id, byte[] data)
        {
            int len = data == null ? 0 : data.Length;
            byte[] pkt = new byte[16 + len];
            pkt[0] = (byte)'O'; pkt[1] = (byte)'R'; pkt[2] = (byte)'G'; pkt[3] = (byte)'B';
            Array.Copy(BitConverter.GetBytes(dev), 0, pkt, 4, 4);
            Array.Copy(BitConverter.GetBytes(id), 0, pkt, 8, 4);
            Array.Copy(BitConverter.GetBytes((uint)len), 0, pkt, 12, 4);
            if (len > 0) Array.Copy(data, 0, pkt, 16, len);
            lock (sync) stream.Write(pkt, 0, pkt.Length);
        }

        void ReadExact(byte[] buf, int count)
        {
            int got = 0;
            while (got < count)
            {
                int n = stream.Read(buf, got, count - got);
                if (n <= 0) throw new IOException("Connection closed");
                got += n;
            }
        }

        // Reads one packet; returns its id and payload.
        uint ReadPacket(out byte[] data)
        {
            byte[] hdr = new byte[16];
            ReadExact(hdr, 16);
            if (hdr[0] != 'O' || hdr[1] != 'R' || hdr[2] != 'G' || hdr[3] != 'B')
                throw new IOException("Bad packet header");
            uint id = BitConverter.ToUInt32(hdr, 8);
            uint size = BitConverter.ToUInt32(hdr, 12);
            data = new byte[size];
            if (size > 0) ReadExact(data, (int)size);
            if (id == PKT_DEVICE_LIST_UPDATED) { DeviceListChanged = true; LastListUpdate = DateTime.UtcNow; }
            return id;
        }

        byte[] WaitFor(uint wanted, int timeoutMs)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < end)
            {
                byte[] data;
                if (ReadPacket(out data) == wanted) return data;
            }
            return null;
        }

        // Consume any unsolicited packets (device list notifications) so the socket never backs up.
        public void Pump()
        {
            if (stream == null) return;
            while (tcp.Available >= 16)
            {
                byte[] data;
                ReadPacket(out data);
            }
        }

        public List<RgbDevice> LoadDevices()
        {
            Pump();
            DeviceListChanged = false;
            Send(0, PKT_CONTROLLER_COUNT, null);
            byte[] resp = WaitFor(PKT_CONTROLLER_COUNT, 5000);
            int count = resp == null ? 0 : (int)BitConverter.ToUInt32(resp, 0);
            List<RgbDevice> list = new List<RgbDevice>();
            for (int i = 0; i < count; i++)
            {
                Send((uint)i, PKT_CONTROLLER_DATA, Protocol > 0 ? BitConverter.GetBytes(Protocol) : null);
                byte[] d = WaitFor(PKT_CONTROLLER_DATA, 10000);
                if (d == null) continue;
                try
                {
                    RgbDevice dev = ParseController(d, Protocol);
                    dev.Index = i;
                    list.Add(dev);
                }
                catch { }
            }
            return list;
        }

        public void ResizeZone(int dev, int zone, int size)
        {
            byte[] d = new byte[8];
            Array.Copy(BitConverter.GetBytes(zone), 0, d, 0, 4);
            Array.Copy(BitConverter.GetBytes(size), 0, d, 4, 4);
            Send((uint)dev, PKT_RESIZE_ZONE, d);
        }

        public void SetCustomMode(int dev)
        {
            Send((uint)dev, PKT_SET_CUSTOM_MODE, null);
        }

        public void UpdateLeds(int dev, Rgb[] colors)
        {
            int size = 4 + 2 + colors.Length * 4;
            byte[] d = new byte[size];
            Array.Copy(BitConverter.GetBytes((uint)size), 0, d, 0, 4);
            Array.Copy(BitConverter.GetBytes((ushort)colors.Length), 0, d, 4, 2);
            int o = 6;
            for (int i = 0; i < colors.Length; i++)
            {
                d[o] = colors[i].R; d[o + 1] = colors[i].G; d[o + 2] = colors[i].B; d[o + 3] = 0;
                o += 4;
            }
            Send((uint)dev, PKT_UPDATE_LEDS, d);
        }

        public void UpdateMode(int dev, int modeIdx, RgbMode m)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            w.Write((uint)0);
            w.Write(modeIdx);
            WriteString(w, m.Name);
            w.Write(m.Value);
            w.Write(m.Flags);
            w.Write(m.SpeedMin);
            w.Write(m.SpeedMax);
            if (Protocol >= 3) { w.Write(m.BrightnessMin); w.Write(m.BrightnessMax); }
            w.Write(m.ColorsMin);
            w.Write(m.ColorsMax);
            w.Write(m.Speed);
            if (Protocol >= 3) w.Write(m.Brightness);
            w.Write(m.Direction);
            w.Write(m.ColorMode);
            w.Write((ushort)m.Colors.Length);
            foreach (Rgb c in m.Colors) { w.Write(c.R); w.Write(c.G); w.Write(c.B); w.Write((byte)0); }
            w.Flush();
            byte[] d = ms.ToArray();
            Array.Copy(BitConverter.GetBytes((uint)d.Length), 0, d, 0, 4);
            Send((uint)dev, PKT_UPDATE_MODE, d);
        }

        static void WriteString(BinaryWriter w, string s)
        {
            byte[] b = Encoding.ASCII.GetBytes((s ?? "") + "\0");
            w.Write((ushort)b.Length);
            w.Write(b);
        }

        static string ReadString(BinaryReader r)
        {
            ushort len = r.ReadUInt16();
            byte[] b = r.ReadBytes(len);
            return Encoding.UTF8.GetString(b).TrimEnd('\0');
        }

        static RgbDevice ParseController(byte[] data, uint proto)
        {
            BinaryReader r = new BinaryReader(new MemoryStream(data));
            RgbDevice d = new RgbDevice();
            r.ReadUInt32(); // data size
            d.Type = r.ReadInt32();
            d.Name = ReadString(r);
            if (proto >= 1) d.Vendor = ReadString(r);
            d.Description = ReadString(r);
            d.Version = ReadString(r);
            d.Serial = ReadString(r);
            d.Location = ReadString(r);

            ushort numModes = r.ReadUInt16();
            d.ActiveMode = r.ReadInt32();
            for (int i = 0; i < numModes; i++)
            {
                RgbMode m = new RgbMode();
                m.Name = ReadString(r);
                m.Value = r.ReadInt32();
                m.Flags = r.ReadUInt32();
                m.SpeedMin = r.ReadUInt32();
                m.SpeedMax = r.ReadUInt32();
                if (proto >= 3) { m.BrightnessMin = r.ReadUInt32(); m.BrightnessMax = r.ReadUInt32(); }
                m.ColorsMin = r.ReadUInt32();
                m.ColorsMax = r.ReadUInt32();
                m.Speed = r.ReadUInt32();
                if (proto >= 3) m.Brightness = r.ReadUInt32();
                m.Direction = r.ReadUInt32();
                m.ColorMode = r.ReadUInt32();
                ushort nc = r.ReadUInt16();
                m.Colors = new Rgb[nc];
                for (int c = 0; c < nc; c++) { byte[] b = r.ReadBytes(4); m.Colors[c] = new Rgb(b[0], b[1], b[2]); }
                d.Modes.Add(m);
            }

            ushort numZones = r.ReadUInt16();
            for (int i = 0; i < numZones; i++)
            {
                RgbZone z = new RgbZone();
                z.Name = ReadString(r);
                z.Type = r.ReadInt32();
                z.LedsMin = r.ReadUInt32();
                z.LedsMax = r.ReadUInt32();
                z.LedsCount = r.ReadUInt32();
                ushort matrixLen = r.ReadUInt16();
                if (matrixLen > 0) r.ReadBytes(matrixLen);
                if (proto >= 4)
                {
                    ushort segs = r.ReadUInt16();
                    for (int s = 0; s < segs; s++)
                    {
                        ReadString(r); r.ReadInt32(); r.ReadUInt32(); r.ReadUInt32();
                    }
                }
                d.Zones.Add(z);
            }

            ushort numLeds = r.ReadUInt16();
            for (int i = 0; i < numLeds; i++)
            {
                d.LedNames.Add(ReadString(r));
                r.ReadUInt32();
            }
            if (r.BaseStream.Position + 2 <= r.BaseStream.Length)
            {
                ushort numColors = r.ReadUInt16();
                for (int i = 0; i < numColors; i++) { byte[] b = r.ReadBytes(4); d.Colors.Add(new Rgb(b[0], b[1], b[2])); }
            }
            return d;
        }
    }

    public struct Rgb
    {
        public byte R, G, B;
        public Rgb(byte r, byte g, byte b) { R = r; G = g; B = b; }
        public Rgb(int r, int g, int b)
        {
            R = (byte)Math.Max(0, Math.Min(255, r));
            G = (byte)Math.Max(0, Math.Min(255, g));
            B = (byte)Math.Max(0, Math.Min(255, b));
        }
        public static Rgb FromColor(System.Drawing.Color c) { return new Rgb(c.R, c.G, c.B); }
        public System.Drawing.Color ToColor() { return System.Drawing.Color.FromArgb(R, G, B); }
    }

    public class RgbMode
    {
        public const uint FLAG_SPEED = 1;
        // The device writes every change into its own memory, which wears out if it's rewritten constantly.
        public const uint FLAG_AUTOMATIC_SAVE = 512;
        public const uint FLAG_PER_LED = 32;
        public const uint FLAG_MODE_COLOR = 64;
        public const uint FLAG_BRIGHTNESS = 16;
        public const uint COLOR_MODE_PER_LED = 1;
        public const uint COLOR_MODE_SPECIFIC = 2;

        public string Name;
        public int Value;
        public uint Flags, SpeedMin, SpeedMax, BrightnessMin, BrightnessMax, ColorsMin, ColorsMax;
        public uint Speed, Brightness, Direction, ColorMode;
        public Rgb[] Colors = new Rgb[0];
    }

    public class RgbZone
    {
        public string Name;
        public int Type;
        public uint LedsMin, LedsMax, LedsCount;
    }

    public class RgbDevice
    {
        public int Index;
        public int Type;
        public string Name = "", Vendor = "", Description = "", Version = "", Serial = "", Location = "";
        public int ActiveMode;
        public List<RgbMode> Modes = new List<RgbMode>();
        public List<RgbZone> Zones = new List<RgbZone>();
        public List<string> LedNames = new List<string>();
        public List<Rgb> Colors = new List<Rgb>();

        public int LedCount { get { return LedNames.Count; } }

        public int LitZoneCount
        {
            get
            {
                int n = 0;
                foreach (RgbZone z in Zones) if (z.LedsCount > 0) n++;
                return n;
            }
        }

        public bool SupportsDirect
        {
            get
            {
                foreach (RgbMode m in Modes)
                    if ((m.Flags & RgbMode.FLAG_PER_LED) != 0 && IsDirectName(m.Name)) return true;
                return false;
            }
        }

        // Re-selecting Direct on a device that's already in it makes many controllers (MSI, Glorious) reset and flash.
        public bool InDirectMode
        {
            get
            {
                if (ActiveMode < 0 || ActiveMode >= Modes.Count) return false;
                RgbMode m = Modes[ActiveMode];
                return (m.Flags & RgbMode.FLAG_PER_LED) != 0 && IsDirectName(m.Name);
            }
        }

        static bool IsDirectName(string n)
        {
            n = (n ?? "").ToLowerInvariant();
            return n == "direct" || n == "custom" || n == "static" || n == "per-led" || n == "per led";
        }

        // Index of the mode used for streaming colours (Direct, or the per-LED Custom/Static stand-in), or -1.
        public int StreamModeIndex
        {
            get
            {
                for (int i = 0; i < Modes.Count; i++)
                    if ((Modes[i].Flags & RgbMode.FLAG_PER_LED) != 0 && IsDirectName(Modes[i].Name)) return i;
                return -1;
            }
        }

        // First mode whose name contains one of the words (in order of preference), or -1.
        public int FindMode(params string[] words)
        {
            foreach (string w in words)
                for (int i = 0; i < Modes.Count; i++)
                    if ((Modes[i].Name ?? "").ToLowerInvariant().Contains(w)) return i;
            return -1;
        }

        // Index of a static single-color mode to use for devices that can't do per-LED updates.
        public int FindStaticMode()
        {
            for (int i = 0; i < Modes.Count; i++)
                if ((Modes[i].Name ?? "").ToLowerInvariant() == "static" && (Modes[i].Flags & RgbMode.FLAG_MODE_COLOR) != 0) return i;
            for (int i = 0; i < Modes.Count; i++)
                if ((Modes[i].Flags & RgbMode.FLAG_MODE_COLOR) != 0 && Modes[i].ColorsMin <= 1) return i;
            return -1;
        }

        public string Key
        {
            get { return Name + "|" + Location + "|" + Serial; }
        }

        static readonly string[] TypeNames = {
            "Motherboard", "RAM", "Graphics Card", "Cooler", "LED Strip", "Keyboard", "Mouse",
            "Mousepad", "Headset", "Headset Stand", "Controller", "Light", "Speaker", "Virtual",
            "Storage", "Case", "Microphone", "Accessory", "Keypad", "Laptop", "Monitor"
        };

        public string TypeName
        {
            get { return Type >= 0 && Type < TypeNames.Length ? TypeNames[Type] : "Device"; }
        }
    }
}
