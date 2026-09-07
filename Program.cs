using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace ExternalSkins
{

    class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(IntPtr hProc, IntPtr baseAddr, [Out] byte[] buffer, IntPtr size, out IntPtr read);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(IntPtr hProc, IntPtr baseAddr, byte[] buffer, IntPtr size, out IntPtr written);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualProtectEx(IntPtr hProc, IntPtr baseAddr, IntPtr size, uint newProtect, out uint oldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualQueryEx(IntPtr hProc, IntPtr addr, out MEMORY_BASIC_INFORMATION info, IntPtr len);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, int BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, out TOKEN_ELEVATION TokenInformation, int TokenInformationLength, out int ReturnLength);

        [StructLayout(LayoutKind.Sequential)]
        struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct TOKEN_ELEVATION
        {
            public int TokenIsElevated;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
        const uint PROCESS_VM_FLAGS = 0x10 | 0x20 | 0x8 | 0x400;
        const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        const uint TOKEN_QUERY = 0x0008;
        const uint SE_PRIVILEGE_ENABLED = 0x0002;

        const uint MEM_COMMIT = 0x1000;
        const uint MEM_IMAGE = 0x1000000;
        const uint PAGE_NOACCESS = 0x01;
        const uint PAGE_GUARD = 0x100;
        const uint PAGE_READWRITE = 0x04;
        const uint PAGE_EXECUTE_READWRITE = 0x40;

        static string skinSuffix = "01 00 00 00 ?? 00 00 ??";

        static Dictionary<string, int> loadedSkins = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        static void Main(string[] args)
        {
            Console.Title = "LEO the cheater";
            Console.WriteLine(">>> builded by LEO enjoy :D ");

            string skinsPath = Path.Combine(AppContext.BaseDirectory, "skins.txt");
            if (!File.Exists(skinsPath))
            {
                skinsPath = Path.Combine(Directory.GetCurrentDirectory(), "skins.txt");
            }

            loadedSkins = LoadSkinsFromFile(skinsPath);
            if (loadedSkins.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[+] Loaded {loadedSkins.Count} skins from skins.txt ({skinsPath})");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"[!] skins.txt not found at '{skinsPath}'. Using raw numeric IDs.");
            }

            if (EnableDebugPrivilege())
            {
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine("[+] SeDebugPrivilege enabled successfully.");
                Console.ResetColor();
            }

            bool isElevated = IsProcessElevated();

            if (!isElevated)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[!] WARNING: Tool is running in non-elevated UAC context.");
                Console.WriteLine("[!] Requesting automatic UAC Administrator elevation...");
                Console.ResetColor();

                try
                {
                    string exePath = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                    {
                        exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    }

                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    Process.Start(startInfo);
                    return;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[-] UAC Elevation failed: {ex.Message}");
                    Console.WriteLine("[!] Please right-click SkinChanger.exe and select 'Run as administrator'.");
                    Console.ResetColor();
                    Console.ReadLine();
                    return;
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[+] Full UAC Administrator privileges verified.");
                Console.ResetColor();
            }

            Console.WriteLine(">>> Searching for emulator process...");

            Process proc = GetTargetProcess();
            if (proc == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[-] FATAL: Emulator process not found. Start BlueStacks/LDPlayer first.");
                Console.ResetColor();
                Console.ReadLine();
                return;
            }

            IntPtr hProc = OpenProcess(PROCESS_ALL_ACCESS, false, proc.Id);
            if (hProc == IntPtr.Zero)
            {
                hProc = OpenProcess(PROCESS_VM_FLAGS, false, proc.Id);
            }

            if (hProc == IntPtr.Zero)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[-] OpenProcess failed. Win32 Error: {Marshal.GetLastWin32Error()}");
                Console.ResetColor();
                Console.ReadLine();
                return;
            }

            long ramMB = proc.WorkingSet64 / (1024 * 1024);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[+] Attached to: {proc.ProcessName} (PID: {proc.Id} | RAM: {ramMB} MB)");
            Console.ResetColor();

            while (true)
            {
                Console.WriteLine("\n=================================================");
                Console.WriteLine("  [1] Change skin with known current ID / Name");
                Console.WriteLine("  [2] Change skin without knowing current ID (wildcard scan)");
                Console.WriteLine("  [3] Scan exact ID / Name only (fast)");
                Console.WriteLine("  [4] Search / List skins from skins.txt");
                Console.WriteLine("  [0] Exit");
                Console.Write("  > ");

                string mode = Console.ReadLine()?.Trim();

                if (mode == "0") break;

                int oldId = 0, newId = 0;

                if (mode == "1")
                {
                    Console.Write("Current Skin (ID or Name): ");
                    string rawOld = Console.ReadLine();
                    Console.Write("New Skin (ID or Name): ");
                    string rawNew = Console.ReadLine();

                    if (!ParseIdOrLookup(rawOld, out oldId) || !ParseIdOrLookup(rawNew, out newId))
                    {
                        Console.WriteLine("[!] Invalid skin specified.");
                        continue;
                    }

                    string pattern = BuildPattern(oldId, true);
                    Console.WriteLine($"[*] Scanning pattern: {pattern}");
                    List<IntPtr> hits = ScanMemory(hProc, pattern, false);
                    if (hits.Count == 0)
                    {
                        Console.WriteLine("[!] No hits with suffix pattern. Trying exact ID fallback...");
                        pattern = BuildPattern(oldId, false);
                        Console.WriteLine($"[*] Scanning exact pattern: {pattern}");
                        hits = ScanMemory(hProc, pattern, false);
                    }
                    ProcessHits(hProc, hits, newId);
                }
                else if (mode == "2")
                {
                    Console.Write("New Skin (ID or Name): ");
                    string rawNew = Console.ReadLine();
                    if (!ParseIdOrLookup(rawNew, out newId))
                    {
                        Console.WriteLine("[!] Invalid skin specified.");
                        continue;
                    }

                    string pattern = "?? ?? ?? ?? " + skinSuffix;
                    Console.WriteLine($"[*] Scanning pattern: {pattern}");
                    List<IntPtr> hits = ScanMemory(hProc, pattern, true);
                    if (hits.Count == 0)
                    {
                        Console.WriteLine("[!] No hits with wildcard. Trying exact ID fallback...");
                        Console.Write("Enter any equipped skin ID or Name (or 0 to cancel): ");
                        string rawOld = Console.ReadLine();
                        if (ParseIdOrLookup(rawOld, out oldId) && oldId > 0)
                        {
                            pattern = BuildPattern(oldId, false);
                            hits = ScanMemory(hProc, pattern, false);
                        }
                    }
                    ProcessHits(hProc, hits, newId);
                }
                else if (mode == "3")
                {
                    Console.Write("Current Skin (ID or Name): ");
                    string rawOld = Console.ReadLine();
                    Console.Write("New Skin (ID or Name): ");
                    string rawNew = Console.ReadLine();
                    if (!ParseIdOrLookup(rawOld, out oldId) || !ParseIdOrLookup(rawNew, out newId))
                    {
                        Console.WriteLine("[!] Invalid skin specified.");
                        continue;
                    }
                    string pattern = BuildPattern(oldId, false);
                    Console.WriteLine($"[*] Scanning pattern: {pattern}");
                    List<IntPtr> hits = ScanMemory(hProc, pattern, false);
                    ProcessHits(hProc, hits, newId);
                }
                else if (mode == "4")
                {
                    SearchAndSelectSkin(hProc);
                }
                else
                {
                    Console.WriteLine("[!] Invalid choice.");
                }
            }

            CloseHandle(hProc);
        }

      
        static Dictionary<string, int> LoadSkinsFromFile(string filePath)
        {
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(filePath))
                return dict;

            try
            {
                string[] lines = File.ReadAllLines(filePath);
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("public const") && trimmed.EndsWith(";"))
                    {
                        int eqIdx = trimmed.IndexOf('=');
                        if (eqIdx > 0)
                        {
                            string left = trimmed.Substring(0, eqIdx).Trim();
                            string right = trimmed.Substring(eqIdx + 1).Replace(";", "").Trim();

                            string[] leftParts = left.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (leftParts.Length >= 4 && int.TryParse(right, out int id))
                            {
                                string skinName = leftParts[3];
                                if (!dict.ContainsKey(skinName))
                                {
                                    dict[skinName] = id;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Error reading skins.txt: {ex.Message}");
            }
            return dict;
        }

      
        static bool ParseIdOrLookup(string input, out int id)
        {
            id = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;

            input = input.Trim();
            if (int.TryParse(input, out id))
            {
                return true;
            }

            if (loadedSkins.TryGetValue(input, out id))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[+] Resolved '{input}' -> ID {id}");
                Console.ResetColor();
                return true;
            }

            var matches = loadedSkins.Where(kvp => kvp.Key.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (matches.Count == 1)
            {
                id = matches[0].Value;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[+] Matched '{matches[0].Key}' -> ID {id}");
                Console.ResetColor();
                return true;
            }
            else if (matches.Count > 1)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[?] Multiple skins matched '{input}':");
                for (int i = 0; i < Math.Min(matches.Count, 10); i++)
                {
                    Console.WriteLine($"    [{i + 1}] {matches[i].Key} (ID: {matches[i].Value})");
                }
                Console.ResetColor();
                Console.Write("Select index (1-10) or 0 to cancel: ");
                string sel = Console.ReadLine();
                if (int.TryParse(sel, out int idx) && idx >= 1 && idx <= Math.Min(matches.Count, 10))
                {
                    id = matches[idx - 1].Value;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[+] Selected '{matches[idx - 1].Key}' -> ID {id}");
                    Console.ResetColor();
                    return true;
                }
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[-] Unknown skin name or ID: '{input}'");
            Console.ResetColor();
            return false;
        }

     
        static void SearchAndSelectSkin(IntPtr hProc)
        {
            if (loadedSkins.Count == 0)
            {
                Console.WriteLine("[!] skins.txt is not loaded or empty.");
                return;
            }

            Console.Write("\nEnter search keyword (e.g. Karambit, AKR, Dragon, Butterfly, AWM): ");
            string query = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(query)) return;

            var matches = loadedSkins.Where(kvp => kvp.Key.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (matches.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[-] No skins found matching '{query}'.");
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n[+] Found {matches.Count} matching skins:");
            for (int i = 0; i < Math.Min(matches.Count, 25); i++)
            {
                Console.WriteLine($"    [{i + 1}] {matches[i].Key} (ID: {matches[i].Value})");
            }
            if (matches.Count > 25)
            {
                Console.WriteLine($"    ... and {matches.Count - 25} more. Refine your search keyword.");
            }
            Console.ResetColor();

            Console.Write("\nSelect skin index to apply (or 0 to cancel): ");
            string choice = Console.ReadLine();
            if (int.TryParse(choice, out int idx) && idx >= 1 && idx <= Math.Min(matches.Count, 25))
            {
                var targetSkin = matches[idx - 1];
                int targetNewId = targetSkin.Value;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[+] Target Skin: {targetSkin.Key} (ID: {targetNewId})");
                Console.ResetColor();

                Console.WriteLine("Patch Options:");
                Console.WriteLine("  [1] Wildcard scan (automatically find equipped skin & replace)");
                Console.WriteLine("  [2] Enter Current Skin ID / Name manually");
                Console.Write("  > ");
                string subMode = Console.ReadLine()?.Trim();

                if (subMode == "1")
                {
                    string pattern = "?? ?? ?? ?? " + skinSuffix;
                    Console.WriteLine($"[*] Scanning pattern: {pattern}");
                    List<IntPtr> hits = ScanMemory(hProc, pattern, true);
                    ProcessHits(hProc, hits, targetNewId);
                }
                else if (subMode == "2")
                {
                    Console.Write("Current Skin (ID or Name): ");
                    string rawOld = Console.ReadLine();
                    if (ParseIdOrLookup(rawOld, out int oldId))
                    {
                        string pattern = BuildPattern(oldId, true);
                        Console.WriteLine($"[*] Scanning pattern: {pattern}");
                        List<IntPtr> hits = ScanMemory(hProc, pattern, false);
                        if (hits.Count == 0)
                        {
                            Console.WriteLine("[!] No hits with suffix pattern. Trying exact ID fallback...");
                            pattern = BuildPattern(oldId, false);
                            hits = ScanMemory(hProc, pattern, false);
                        }
                        ProcessHits(hProc, hits, targetNewId);
                    }
                }
            }
        }

   
        static bool EnableDebugPrivilege()
        {
            try
            {
                IntPtr hToken;
                if (OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out hToken))
                {
                    TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES();
                    tp.PrivilegeCount = 1;
                    tp.Attributes = SE_PRIVILEGE_ENABLED;
                    if (LookupPrivilegeValue(null, "SeDebugPrivilege", out tp.Luid))
                    {
                        bool ok = AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                        CloseHandle(hToken);
                        return ok;
                    }
                    CloseHandle(hToken);
                }
            }
            catch { }
            return false;
        }

       
        static bool IsProcessElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    TOKEN_ELEVATION elevation;
                    int size = Marshal.SizeOf(typeof(TOKEN_ELEVATION));
                    if (GetTokenInformation(identity.Token, 20 /* TokenElevation */, out elevation, size, out _))
                    {
                        return elevation.TokenIsElevated != 0;
                    }
                }
            }
            catch { }
            return false;
        }

       
        static string BuildPattern(int id, bool withSuffix)
        {
            byte[] idBytes = BitConverter.GetBytes(id);
            string idHex = BitConverter.ToString(idBytes).Replace("-", " ");
            if (withSuffix)
                return idHex + " " + skinSuffix;
            else
                return idHex;
        }

     
        static List<IntPtr> ScanMemory(IntPtr hProc, string pattern, bool isWildcardScan)
        {
            var results = new List<IntPtr>();
            var split = pattern.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            byte?[] pBytes = new byte?[split.Length];

            for (int i = 0; i < split.Length; i++)
            {
                if (split[i] == "??")
                    pBytes[i] = null;
                else
                    pBytes[i] = byte.Parse(split[i], NumberStyles.HexNumber);
            }

            bool is32Bit = false;
            try
            {
                IsWow64Process(hProc, out is32Bit);
            }
            catch { }

            long maxAddress = is32Bit ? 0x80000000L : 0x7FFFFFFFFFFF;

            long current = 0;
            MEMORY_BASIC_INFORMATION mbi;
            int regionCount = 0;
            int scannedRegions = 0;
            int readErrors = 0;
            Stopwatch sw = Stopwatch.StartNew();
            int mbiSize = Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION));

            int debugCount = 0;
            while (current < maxAddress && VirtualQueryEx(hProc, (IntPtr)current, out mbi, (IntPtr)mbiSize) != IntPtr.Zero)
            {
                regionCount++;
                long regionSize = mbi.RegionSize.ToInt64();

                if (regionSize <= 0)
                {
                    current += 0x1000;
                    continue;
                }

                if (mbi.State == MEM_COMMIT)
                {
                  
                    if (mbi.Type == MEM_IMAGE)
                    {
                        current = mbi.BaseAddress.ToInt64() + regionSize;
                        continue;
                    }

                    if ((mbi.Protect & PAGE_NOACCESS) != 0 || (mbi.Protect & PAGE_GUARD) != 0)
                    {
                        current = mbi.BaseAddress.ToInt64() + regionSize;
                        continue;
                    }
                    long baseAddr = mbi.BaseAddress.ToInt64();
                    bool regionHasRead = false;

                    const int chunkSize = 4 * 1024 * 1024;
                    int overlap = pBytes.Length - 1;

                    long offset = 0;
                    while (offset < regionSize)
                    {
                        long currentChunkAddr = baseAddr + offset;
                        long bytesLeft = regionSize - offset;
                        int bytesToRead = (int)Math.Min(chunkSize + overlap, bytesLeft);

                        byte[] chunkBuffer = new byte[bytesToRead];
                        IntPtr bytesReadPtr;

                        bool readOk = ReadProcessMemory(hProc, (IntPtr)currentChunkAddr, chunkBuffer, (IntPtr)bytesToRead, out bytesReadPtr);
                        int err1 = readOk ? 0 : Marshal.GetLastWin32Error();

                        if (!readOk)
                        {
                           
                            uint oldProtect;
                            if (VirtualProtectEx(hProc, (IntPtr)currentChunkAddr, (IntPtr)bytesToRead, PAGE_EXECUTE_READWRITE, out oldProtect))
                            {
                                readOk = ReadProcessMemory(hProc, (IntPtr)currentChunkAddr, chunkBuffer, (IntPtr)bytesToRead, out bytesReadPtr);
                                VirtualProtectEx(hProc, (IntPtr)currentChunkAddr, (IntPtr)bytesToRead, oldProtect, out _);
                            }
                        }

                        if (debugCount < 15)
                        {
                            debugCount++;
                            Console.WriteLine($"[DBG #{debugCount}] Addr: 0x{currentChunkAddr:X} | Size: {bytesToRead} | Protect: 0x{mbi.Protect:X} | Type: 0x{mbi.Type:X} | ReadOk: {readOk} | Err: {err1}");
                        }

                        if (readOk)
                        {
                            regionHasRead = true;
                            int bytesRead = bytesReadPtr.ToInt32() > 0 ? bytesReadPtr.ToInt32() : bytesToRead;
                            ScanChunkBuffer(chunkBuffer, bytesRead, currentChunkAddr, pBytes, isWildcardScan, results);
                        }
                        else
                        {
                            readErrors++;
                            const int pageSize = 4096;
                            bool chunkHasAnyPage = false;
                            for (int pageOffset = 0; pageOffset < bytesToRead; pageOffset += pageSize)
                            {
                                int pageToRead = Math.Min(pageSize, bytesToRead - pageOffset);
                                byte[] pageBuffer = new byte[pageToRead];
                                IntPtr pageAddr = (IntPtr)(currentChunkAddr + pageOffset);
                                IntPtr pageReadPtr;

                                bool pageOk = ReadProcessMemory(hProc, pageAddr, pageBuffer, (IntPtr)pageToRead, out pageReadPtr);

                                if (!pageOk)
                                {
                                    uint oldProtect;
                                    if (VirtualProtectEx(hProc, pageAddr, (IntPtr)pageToRead, PAGE_EXECUTE_READWRITE, out oldProtect))
                                    {
                                        pageOk = ReadProcessMemory(hProc, pageAddr, pageBuffer, (IntPtr)pageToRead, out pageReadPtr);
                                        VirtualProtectEx(hProc, pageAddr, (IntPtr)pageToRead, oldProtect, out _);
                                    }
                                }

                                if (pageOk)
                                {
                                    int pRead = pageReadPtr.ToInt32() > 0 ? pageReadPtr.ToInt32() : pageToRead;
                                    Array.Copy(pageBuffer, 0, chunkBuffer, pageOffset, pRead);
                                    regionHasRead = true;
                                    chunkHasAnyPage = true;
                                }
                            }

                            if (chunkHasAnyPage)
                            {
                                ScanChunkBuffer(chunkBuffer, bytesToRead, currentChunkAddr, pBytes, isWildcardScan, results);
                            }
                        }

                        offset += chunkSize;
                    }

                    if (regionHasRead)
                    {
                        scannedRegions++;
                    }
                }

                current = mbi.BaseAddress.ToInt64() + regionSize;

                if (regionCount % 500 == 0 && regionCount > 0)
                {
                    Console.WriteLine($"[DBG] Regions: {regionCount} | Scanned: {scannedRegions} | Hits: {results.Count} | Time: {sw.Elapsed.TotalSeconds:F1}s");
                }
            }

            sw.Stop();
            Console.WriteLine($"[DBG] Scan complete. Regions: {regionCount} | Scanned: {scannedRegions} | ReadErrors: {readErrors} | Time: {sw.Elapsed.TotalSeconds:F2}s");
            return results;
        }

      
        static void ScanChunkBuffer(byte[] chunkBuffer, int bytesRead, long currentChunkAddr, byte?[] pBytes, bool isWildcardScan, List<IntPtr> results)
        {
            int scanLimit = Math.Min(chunkBuffer.Length, bytesRead) - pBytes.Length;
            for (int i = 0; i <= scanLimit; i++)
            {
                if (MatchPattern(chunkBuffer, i, pBytes))
                {
                    IntPtr hitAddr = (IntPtr)(currentChunkAddr + i);
                    if (isWildcardScan)
                    {
                        int candidateID = BitConverter.ToInt32(chunkBuffer, i);
                        if (candidateID >= 1000 && candidateID <= 500000)
                        {
                            results.Add(hitAddr);
                        }
                    }
                    else
                    {
                        results.Add(hitAddr);
                    }
                }
            }
        }

       
        static bool MatchPattern(byte[] data, int offset, byte?[] pattern)
        {
            if (offset + pattern.Length > data.Length) return false;
            for (int i = 0; i < pattern.Length; i++)
            {
                if (pattern[i].HasValue && data[offset + i] != pattern[i].Value)
                    return false;
            }
            return true;
        }

      
        static bool SafeWriteMemory(IntPtr hProc, IntPtr addr, byte[] data)
        {
            IntPtr written;
            if (WriteProcessMemory(hProc, addr, data, (IntPtr)data.Length, out written) && written.ToInt32() == data.Length)
            {
                return true;
            }

           
            uint oldProtect;
            if (VirtualProtectEx(hProc, addr, (IntPtr)data.Length, PAGE_EXECUTE_READWRITE, out oldProtect))
            {
                bool ok = WriteProcessMemory(hProc, addr, data, (IntPtr)data.Length, out written) && written.ToInt32() == data.Length;
                VirtualProtectEx(hProc, addr, (IntPtr)data.Length, oldProtect, out _);
                return ok;
            }

            return false;
        }

        static void ProcessHits(IntPtr hProc, List<IntPtr> hits, int newId)
        {
            if (hits.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[-] No matches found.");
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[+] Found {hits.Count} matches:");
            for (int i = 0; i < Math.Min(hits.Count, 10); i++)
                Console.WriteLine($"    [{i}] 0x{hits[i].ToString("X")}");
            if (hits.Count > 10)
                Console.WriteLine($"    ... and {hits.Count - 10} more");
            Console.ResetColor();

            Console.WriteLine("\nAction:");
            Console.WriteLine("  [1] Patch ALL");
            Console.WriteLine("  [2] Patch specific index");
            Console.WriteLine("  [0] Cancel");
            Console.Write("  > ");
            string choice = Console.ReadLine();

            if (choice == "1")
            {
                int success = 0;
                byte[] patch = BitConverter.GetBytes(newId);
                foreach (var addr in hits)
                {
                    if (SafeWriteMemory(hProc, addr, patch))
                        success++;
                }
                Console.WriteLine($"[++] Patched {success}/{hits.Count} addresses.");
            }
            else if (choice == "2")
            {
                Console.Write("Index: ");
                string idxStr = Console.ReadLine();
                int idx;
                if (int.TryParse(idxStr, out idx) && idx >= 0 && idx < hits.Count)
                {
                    byte[] patch = BitConverter.GetBytes(newId);
                    if (SafeWriteMemory(hProc, hits[idx], patch))
                        Console.WriteLine($"[+] Patched 0x{hits[idx].ToString("X")}");
                    else
                        Console.WriteLine("[!] Write failed.");
                }
                else
                {
                    Console.WriteLine("[!] Invalid index.");
                }
            }
            else
            {
                Console.WriteLine("[?] Cancelled.");
            }
        }

        static Process GetTargetProcess()
        {
            string[] processNames = { "HD-Player", "LdVBoxHeadless", "Ld9BoxHeadless", "dnplayer", "Nox", "NoxVMHandle", "MEmu", "MEmuHeadless", "BstkSVC" };
            foreach (string name in processNames)
            {
                Process[] procs = Process.GetProcessesByName(name);
                if (procs.Length > 0)
                {
                    return procs.OrderByDescending(p => p.WorkingSet64).First();
                }
            }
            return null;
        }
    }
}