// Exercises the real Windows patch lifecycle against a purpose-built child process.
// It never discovers or opens a game process. The child contains only synthetic code/data.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace MOTrainer336
{
    internal static class PowerOverrideHarness
    {
        private const int Hook = 0x508D81;
        private const int BuildHook = 0x4C9B68;
        private static readonly byte[] BuildOriginal = { 0xE8, 0xC3, 0xCA, 0xF5, 0xFF };
        private static int PlayerA;
        private static int PlayerB;
        private const int Output = 0x53A4;
        private const int Drain = 0x53A8;
        private static readonly byte[] Original = { 0xE8, 0x5A, 0xBF, 0xF4, 0xFF };
        private static int assertions;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protection);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protection, out uint old);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int id);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] bytes, int size, out IntPtr count);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr VirtualQueryEx(IntPtr process, IntPtr address, out MemoryInfo info, UIntPtr size);

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryInfo
        {
            public IntPtr Address;
            public IntPtr AllocationBase;
            public uint AllocationProtection;
            public UIntPtr Size;
            public uint State;
            public uint Protection;
            public uint Type;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Evaluate(int player);

        private static void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name);
            assertions++;
            Console.WriteLine("PASS " + name);
        }

        private static byte[] Read(IntPtr handle, int address, int count)
        {
            byte[] bytes = new byte[count];
            IntPtr read;
            if (!ReadProcessMemory(handle, new IntPtr(address), bytes, count, out read) || read.ToInt32() != count)
                throw new InvalidOperationException("fixture read failed");
            return bytes;
        }

        private static void Map(int address, int size)
        {
            if (VirtualAlloc(new IntPtr(address), new UIntPtr((uint)size), 0x3000, 0x40).ToInt32() != address)
                throw new InvalidOperationException("fixture address unavailable: " + address.ToString("X"));
        }

        private static int Allocate()
        {
            IntPtr block = VirtualAlloc(IntPtr.Zero, new UIntPtr(0x10000), 0x3000, 0x40);
            if (block == IntPtr.Zero) throw new InvalidOperationException("fixture allocation failed");
            return block.ToInt32();
        }

        private static void Bytes(int address, byte[] bytes)
        {
            Marshal.Copy(bytes, 0, new IntPtr(address), bytes.Length);
        }

        private static void Host()
        {
            Map(0x500000, 0x10000);
            Map(0x450000, 0x10000);
            Map(0xA80000, 0x10000);
            Map(0x4C0000, 0x10000);
            Map(0x420000, 0x10000);
            PlayerA = Allocate();
            PlayerB = Allocate();
            int caller = Allocate();
            int buildCaller = Allocate();
            int factory = Allocate();
            Bytes(BuildHook, BuildOriginal);
            Bytes(BuildHook + 5, new byte[] { 0xC3 });
            Bytes(0x426630, new byte[] { 0xC3 });
            // stdcall: ESI=factory, ECX=EDI=timer at original CALL location.
            byte[] buildCallerCode = new byte[] {
                0x56, 0x57, 0x8B, 0x74, 0x24, 0x0C, 0x8D, 0x7E, 0x2C, 0x8B, 0xCF,
                0xB8, 0, 0, 0, 0, 0xFF, 0xD0, 0x5F, 0x5E, 0xC2, 0x04, 0
            };
            Buffer.BlockCopy(BitConverter.GetBytes(BuildHook), 0, buildCallerCode, 12, 4);
            Bytes(buildCaller, buildCallerCode);
            Bytes(Hook, Original);
            // Continuation returns the state observed by the downstream callback.
            Bytes(Hook + 5, new byte[] { 0x8B, 0x86, 0, 0x60, 0, 0, 0xC3 });
            // Stand-in downstream consumer: powered = output >= drain, save the result.
            // This detects patches that run after the downstream callback has already observed low power.
            Bytes(0x454CE0, new byte[] {
                0x8B, 0x81, 0xA4, 0x53, 0, 0, 0x3B, 0x81, 0xA8, 0x53, 0, 0,
                0x0F, 0x9D, 0xC0, 0x0F, 0xB6, 0xC0, 0x89, 0x81, 0, 0x60, 0, 0, 0xC3
            });
            // stdcall Evaluate(player): save ESI; ECX=ESI=player; call original call-site; restore ESI.
            byte[] callerCode = new byte[] {
                0x56, 0x8B, 0x74, 0x24, 0x08, 0x8B, 0xCE,
                0xB8, 0x81, 0x8D, 0x50, 0, 0xFF, 0xD0, 0x5E, 0xC2, 0x04, 0
            };
            Buffer.BlockCopy(BitConverter.GetBytes(Hook), 0, callerCode, 8, 4);
            Bytes(caller, callerCode);
            uint old;
            VirtualProtect(new IntPtr(0x500000), new UIntPtr(0x10000), 0x20, out old);
            VirtualProtect(new IntPtr(0x450000), new UIntPtr(0x10000), 0x20, out old);
            VirtualProtect(new IntPtr(caller), new UIntPtr(0x10000), 0x20, out old);
            VirtualProtect(new IntPtr(0x4C0000), new UIntPtr(0x10000), 0x20, out old);
            VirtualProtect(new IntPtr(0x420000), new UIntPtr(0x10000), 0x20, out old);
            VirtualProtect(new IntPtr(buildCaller), new UIntPtr(0x10000), 0x20, out old);
            Marshal.WriteInt32(new IntPtr(0xA83D4C), PlayerA);
            Evaluate evaluate = (Evaluate)Marshal.GetDelegateForFunctionPointer(new IntPtr(caller), typeof(Evaluate));
            Evaluate build = (Evaluate)Marshal.GetDelegateForFunctionPointer(new IntPtr(buildCaller), typeof(Evaluate));
            Console.WriteLine("READY," + PlayerA + "," + PlayerB);
            string line;
            while ((line = Console.ReadLine()) != null)
            {
                // .NET Framework can prepend a UTF-8 BOM to the redirected
                // stdin pipe on UTF-8 desktops such as GitHub's Windows runner.
                string[] parts = line.TrimStart('\uFEFF').Split(' ');
                int player = parts.Length > 1 && parts[1] == "B" ? PlayerB : PlayerA;
                if (parts[0] == "quit") break;
                if (parts[0] == "eval")
                {
                    Marshal.WriteInt32(new IntPtr(player + Output), 50);
                    Marshal.WriteInt32(new IntPtr(player + Drain), 200);
                    int result = evaluate(player);
                    Console.WriteLine(Marshal.ReadInt32(new IntPtr(player + Output)) + "," + Marshal.ReadInt32(new IntPtr(player + Drain)) + "," + result);
                }
                else if (parts[0] == "build")
                {
                    Marshal.WriteInt32(new IntPtr(factory + 0x6C), player);
                    Marshal.WriteInt32(new IntPtr(factory + 0x58), player); // opaque non-null object
                    Marshal.WriteInt32(new IntPtr(factory + 0x24), 0);
                    Marshal.WriteInt32(new IntPtr(factory + 0x34), 200);
                    Marshal.WriteInt32(new IntPtr(factory + 0x38), 200);
                    Marshal.WriteInt32(new IntPtr(factory + 0x3C), 1);
                    Marshal.WriteByte(new IntPtr(factory + 0x70), 0);
                    build(factory);
                    Console.WriteLine(Marshal.ReadInt32(new IntPtr(factory + 0x24)) + "," + Marshal.ReadInt32(new IntPtr(factory + 0x34)));
                }
                else if (parts[0] == "player")
                {
                    Marshal.WriteInt32(new IntPtr(0xA83D4C), parts[1] == "none" ? 0 : player);
                    Console.WriteLine("OK");
                }
                else if (parts[0] == "flags")
                    Console.WriteLine(Marshal.ReadByte(new IntPtr(player + 0x5778)) + "," + Marshal.ReadByte(new IntPtr(player + 0x5779)));
                else if (parts[0] == "corrupt" || parts[0] == "corruptbuild")
                {
                    int address = parts[0] == "corrupt" ? Hook : BuildHook;
                    VirtualProtect(new IntPtr(address), new UIntPtr(5), 0x40, out old);
                    Bytes(address, new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90 });
                    VirtualProtect(new IntPtr(address), new UIntPtr(5), old, out old);
                    Console.WriteLine("OK");
                }
                else if (parts[0] == "blockbuildflag" || parts[0] == "allowbuildflag")
                {
                    int cave = unchecked(BuildHook + 5 + Marshal.ReadInt32(new IntPtr(BuildHook + 1)));
                    if (!VirtualProtect(new IntPtr(cave + 4096), new UIntPtr(4096), parts[0] == "blockbuildflag" ? 0x01u : 0x04u, out old))
                        throw new InvalidOperationException("fixture build flag protection failed");
                    Console.WriteLine("OK");
                }
                else if (parts[0] == "blockflags" || parts[0] == "allowflags")
                {
                    // PAGE_NOACCESS forces WriteProcessMemory to fail at the
                    // recalculation flags without touching the patch control page.
                    if (!VirtualProtect(new IntPtr(player + 0x5000), new UIntPtr(0x1000), parts[0] == "blockflags" ? 0x01u : 0x04u, out old))
                        throw new InvalidOperationException("fixture flags protection failed");
                    Console.WriteLine("OK");
                }
                else Console.WriteLine("OK");
            }
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly Process Child;
            internal readonly IntPtr Handle;
            internal readonly PowerOverride Patch;
            internal readonly BuildOverride BuildPatch;
            internal Fixture()
            {
                ProcessStartInfo start = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "host");
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.WindowStyle = ProcessWindowStyle.Hidden;
                start.RedirectStandardInput = true;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                start.StandardOutputEncoding = new UTF8Encoding(false);
                start.StandardErrorEncoding = new UTF8Encoding(false);
                Child = Process.Start(start);
                string ready = Child.StandardOutput.ReadLine();
                if (ready == null || !ready.StartsWith("READY,"))
                    throw new InvalidOperationException("fixture startup failed: " + Child.StandardError.ReadToEnd());
                string[] addresses = ready.Split(',');
                PlayerA = Int32.Parse(addresses[1]);
                PlayerB = Int32.Parse(addresses[2]);
                Handle = OpenProcess(1082, false, Child.Id);
                if (Handle == IntPtr.Zero) throw new InvalidOperationException("fixture process open failed");
                Patch = new PowerOverride(Child, Handle);
                BuildPatch = new BuildOverride(Child, Handle);
            }
            internal string Send(string text)
            {
                Child.StandardInput.WriteLine(text);
                Child.StandardInput.Flush();
                string response = Child.StandardOutput.ReadLine();
                if (response == null)
                {
                    Child.WaitForExit(3000);
                    throw new InvalidOperationException("fixture ended after '" + text + "', exit " +
                        (Child.HasExited ? Child.ExitCode.ToString("X8") : "pending") + ": " + Child.StandardError.ReadToEnd());
                }
                return response;
            }
            public void Dispose()
            {
                if (!Child.HasExited)
                {
                    Child.StandardInput.WriteLine("quit");
                    Child.StandardInput.Flush();
                    if (!Child.WaitForExit(3000)) Child.Kill();
                }
                CloseHandle(Handle);
                Child.Dispose();
            }
        }

        private static void Lifecycle()
        {
            string error;
            using (Fixture host = new Fixture())
            {
                string baseline = host.Send("\uFEFFbuild A");
                Check(baseline == "0,200", "build baseline retains natural production (observed: " + baseline + ")");
                Check(host.BuildPatch.SetEnabled(true, out error), "enable instant build: " + error);
                Check(host.Send("build A") == "53,0", "build advances player to final production step");
                Check(host.Send("build B") == "0,200", "build does not accelerate AI");
                byte[] jump = Read(host.Handle, BuildHook, 5);
                Check(jump[0] == 0xE9, "build installs its own CALL replacement");
                Check(host.BuildPatch.SetEnabled(true, out error), "repeat build enable succeeds");
                Check(BitConverter.ToString(jump) == BitConverter.ToString(Read(host.Handle, BuildHook, 5)), "build reuses allocation");
                Check(host.Patch.SetEnabled(true, PlayerA, out error), "power enables alongside build");
                Check(host.Send("eval A") == "1000000,0,1", "power works alongside build");
                Check(host.Send("build A") == "53,0", "build works alongside power");
                host.Send("player B");
                Check(host.Send("build B") == "53,0", "build follows new current player without stale pointers");
                Check(host.Send("build A") == "0,200", "build stops accelerating old player");
                host.Send("player none");
                Check(host.Send("build B") == "0,200", "no current player means no accelerated production");
                Check(host.BuildPatch.SetEnabled(false, out error), "build disables outside match");
                host.Send("player A");
                Check(host.Send("build A") == "0,200", "disabled build leaves timer and progress unchanged");
                Check(host.BuildPatch.SetEnabled(true, out error), "build re-enables");
                Check(host.BuildPatch.Detach(out error), "build detaches successfully");
                Check(BitConverter.ToString(Read(host.Handle, BuildHook, 5)) == BitConverter.ToString(BuildOriginal), "build detach restores exact instruction");
                Check(host.Send("eval A") == "1000000,0,1", "detaching build preserves power hook");
                Check(host.Patch.Detach(out error), "power detaches after build");
                Check(host.Send("build A") == "0,200", "production keeps running after both detach");
            }
            using (Fixture host = new Fixture())
            {
                Check(host.Send("eval A") == "50,200,0", "baseline consumer sees insufficient power");
                Check(host.Patch.SetEnabled(true, PlayerA, out error), "enable: " + error);
                Check(host.Send("flags A") == "1,1", "enable requests power and radar recalculation");
                Check(host.Send("eval A") == "1000000,0,1", "player powered before downstream consumer runs");
                Check(host.Send("eval B") == "50,200,0", "AI retains natural power and consumer state");
                byte[] jump = Read(host.Handle, Hook, 5);
                Check(jump[0] == 0xE9, "call-site replaced by JMP");
                int cave = unchecked(Hook + 5 + BitConverter.ToInt32(jump, 1));
                MemoryInfo info;
                VirtualQueryEx(host.Handle, new IntPtr(cave), out info, new UIntPtr((uint)Marshal.SizeOf(typeof(MemoryInfo))));
                Check(info.Protection == 0x20, "stub code is RX");
                VirtualQueryEx(host.Handle, new IntPtr(cave + 4096), out info, new UIntPtr((uint)Marshal.SizeOf(typeof(MemoryInfo))));
                Check(info.Protection == 0x04, "enable flag is RW without execute permission");
                Check(host.Patch.SetEnabled(true, PlayerA, out error), "repeat enable is idempotent");
                Check(BitConverter.ToString(Read(host.Handle, Hook, 5)) == BitConverter.ToString(jump), "repeat enable reuses the same stub");
                host.Send("player B");
                Check(host.Send("eval B") == "1000000,0,1", "new current player is selected dynamically");
                Check(host.Send("eval A") == "50,200,0", "previous player is no longer overridden");
                Check(host.Patch.SetEnabled(false, PlayerB, out error), "disable: " + error);
                Check(host.Send("flags B") == "1,1", "disable requests real power and radar recalculation");
                Check(host.Send("eval B") == "50,200,0", "disabled consumer observes natural low power");
                Check(host.Patch.SetEnabled(true, PlayerB, out error), "re-enable: " + error);
                host.Send("player none");
                Check(host.Patch.SetEnabled(false, 0, out error), "disable works outside a match");
                host.Send("player B");
                Check(host.Patch.Detach(out error), "detach: " + error);
                Check(BitConverter.ToString(Read(host.Handle, Hook, 5)) == BitConverter.ToString(Original), "detach restores exact original instruction");
                Check(host.Send("eval B") == "50,200,0", "child keeps running after detach");
                Check(host.Patch.Detach(out error), "detach is idempotent");
            }
            using (Fixture host = new Fixture())
            {
                host.Send("corrupt");
                Check(!host.Patch.SetEnabled(true, PlayerA, out error), "unknown original instruction is rejected");
                Check(BitConverter.ToString(Read(host.Handle, Hook, 5)) == "90-90-90-90-90", "rejection leaves unknown code untouched");
                Check(host.Send("ping") == "OK", "rejected install does not leave child threads suspended");
            }
            using (Fixture host = new Fixture())
            {
                host.Send("blockflags A");
                Check(!host.Patch.SetEnabled(true, PlayerA, out error), "enable reports recalculation-write failure");
                Check(!host.Patch.MayBeEnabled, "failed enable rolls back the remote active flag");
                Check(host.Patch.NeedsRecovery, "failed recalculation retains recovery state");
                host.Send("allowflags A");
                Check(host.Patch.Detach(out error), "recovery retries after flags become writable");
                Check(!host.Patch.NeedsRecovery, "successful recovery clears pending state");
                Check(host.Send("eval A") == "50,200,0", "failed enable never leaves an active override");
            }
            using (Fixture host = new Fixture())
            {
                Check(host.Patch.SetEnabled(true, PlayerA, out error), "enable before simulated third-party change");
                host.Send("corrupt");
                Check(!host.Patch.Detach(out error), "detach refuses to overwrite third-party code");
                Check(BitConverter.ToString(Read(host.Handle, Hook, 5)) == "90-90-90-90-90", "third-party code survives detach refusal");
                Check(host.Send("ping") == "OK", "detach failure resumes child threads");
            }
            using (Fixture host = new Fixture())
            {
                host.Send("corruptbuild");
                Check(!host.BuildPatch.SetEnabled(true, out error), "build rejects unknown original instruction");
                Check(!host.BuildPatch.MayBeEnabled, "rejected build enable remains OFF");
                Check(BitConverter.ToString(Read(host.Handle, BuildHook, 5)) == "90-90-90-90-90", "build rejection preserves other patch");
                Check(host.Send("ping") == "OK", "build rejection leaves child running");
            }
            using (Fixture host = new Fixture())
            {
                Check(host.BuildPatch.SetEnabled(true, out error), "build enables before flag failure");
                host.Send("blockbuildflag");
                Check(!host.BuildPatch.SetEnabled(false, out error), "failed build disable is reported");
                Check(host.BuildPatch.MayBeEnabled && host.BuildPatch.NeedsRecovery, "failed disable retains honest state for UI/recovery");
                host.Send("allowbuildflag");
                Check(host.BuildPatch.Detach(out error), "build recovers after flag becomes writable");
                Check(!host.BuildPatch.MayBeEnabled && !host.BuildPatch.NeedsRecovery, "recovered build is OFF without pending recovery");
                Check(host.Send("build A") == "0,200", "recovered build restores natural production");
            }
            Console.WriteLine("Completed " + assertions + " native lifecycle assertions; no game process was used.");
        }

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Console.InputEncoding = new UTF8Encoding(false);
                Console.OutputEncoding = new UTF8Encoding(false);
                if (args.Length != 0 && args[0] == "host") Host();
                else if (args.Length == 2 && args[0] == "emit")
                {
                    byte[] code = PowerOverride.BuildStub(0x2000000, 0x2001000);
                    string hex = BitConverter.ToString(code).Replace("-", "");
                    File.WriteAllText(args[1], "{\"code_address\":33554432,\"enabled_address\":33558528,\"hook_address\":5279105,\"return_address\":5279110,\"code_hex\":\"" + hex + "\"}");
                }
                else if (args.Length == 2 && args[0] == "emit-build")
                {
                    byte[] code = BuildOverride.BuildStub(0x2000000, 0x2001000);
                    string hex = BitConverter.ToString(code).Replace("-", "");
                    File.WriteAllText(args[1], "{\"code_address\":33554432,\"enabled_address\":33558528,\"hook_address\":5020520,\"return_address\":5020525,\"code_hex\":\"" + hex + "\"}");
                }
                else Lifecycle();
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
                return 1;
            }
        }
    }
}
