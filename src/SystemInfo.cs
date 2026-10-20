using System;
using System.Runtime.InteropServices;

namespace RGBUnify
{
    // What kind of PC this is (read once, in the background where it matters - WMI can take a second).
    public static class SystemInfo
    {
        static bool loaded;
        static bool laptop;
        static string maker = "", model = "", boardMaker = "";

        public static bool IsLaptop { get { Load(); return laptop; } }
        public static string Maker { get { Load(); return maker; } }
        public static string Model { get { Load(); return model; } }
        public static string BoardMaker { get { Load(); return boardMaker; } }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                using (System.Management.ManagementObjectSearcher s = new System.Management.ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
                    foreach (System.Management.ManagementObject o in s.Get())
                    {
                        ushort[] types = o["ChassisTypes"] as ushort[];
                        if (types == null) continue;
                        // Portable, laptop, notebook, sub-notebook, convertible, detachable...
                        foreach (ushort t in types) if (Array.IndexOf(new ushort[] { 8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32 }, t) >= 0) laptop = true;
                    }
                using (System.Management.ManagementObjectSearcher s = new System.Management.ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
                    foreach (System.Management.ManagementObject o in s.Get())
                    {
                        maker = ((o["Manufacturer"] as string) ?? "").Trim();
                        model = ((o["Model"] as string) ?? "").Trim();
                    }
                using (System.Management.ManagementObjectSearcher s = new System.Management.ManagementObjectSearcher("SELECT Manufacturer FROM Win32_BaseBoard"))
                    foreach (System.Management.ManagementObject o in s.Get())
                        boardMaker = ((o["Manufacturer"] as string) ?? "").Trim();
            }
            catch { }
        }

        public static bool MadeBy(string brand)
        {
            return Maker.IndexOf(brand, StringComparison.OrdinalIgnoreCase) >= 0 || BoardMaker.IndexOf(brand, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        [DllImport("kernel32.dll")] static extern uint EnumSystemFirmwareTables(uint provider, byte[] buffer, uint size);

        // True when the BIOS carries a WPBT table: Windows then runs a vendor program from the firmware at every boot,
        // which is how ASUS (Armoury Crate), MSI and Gigabyte reinstall their software after you remove it.
        public static bool HasWpbt()
        {
            try
            {
                const uint ACPI = 0x41435049; // 'ACPI'
                uint size = EnumSystemFirmwareTables(ACPI, null, 0);
                if (size == 0) return false;
                byte[] buf = new byte[size];
                EnumSystemFirmwareTables(ACPI, buf, size);
                for (int i = 0; i + 4 <= buf.Length; i += 4)
                    if (buf[i] == 'W' && buf[i + 1] == 'P' && buf[i + 2] == 'B' && buf[i + 3] == 'T') return true;
            }
            catch { }
            return false;
        }
    }
}
