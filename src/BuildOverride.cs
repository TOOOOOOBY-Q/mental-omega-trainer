using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MOTrainer336
{
    // FactoryClass::Update, after suspension/completion checks and before its
    // timer update. Runs on the game's own thread, so no stale factory pointers
    // or half-written progress can race factory deletion / production changes.
    internal sealed class BuildOverride
    {
        private const int HookAddress = 0x4C9B68;
        private const int ReturnAddress = 0x4C9B6D;
        private const int OriginalCallAddress = 0x426630;
        private const int CurrentPlayerAddress = 0xA83D4C;
        private static readonly byte[] Original = { 0xE8, 0xC3, 0xCA, 0xF5, 0xFF };
        private readonly RemoteCallHook hook;

        internal bool MayBeEnabled { get { return hook.MayBeEnabled; } }
        internal bool NeedsRecovery { get { return hook.NeedsRecovery; } }

        internal BuildOverride(Process process, IntPtr processHandle)
        {
            hook = new RemoteCallHook(process, processHandle, HookAddress, Original, BuildStub);
        }

        internal bool SetEnabled(bool enabled, out string error) { return hook.SetEnabled(enabled, out error); }
        internal bool Detach(out string error) { return hook.Detach(out error); }

        internal static byte[] BuildStub(int codeAddress, int enabledAddress)
        {
            List<byte> code = new List<byte>();
            List<int> skips = new List<int>();
            code.AddRange(new byte[] { 0x9C, 0x50 });       // pushfd; push eax
            code.AddRange(new byte[] { 0x80, 0x3D });       // cmp byte ptr [enabled], 1
            code.AddRange(BitConverter.GetBytes(enabledAddress));
            code.Add(1);
            Skip(code, skips, 0x75);                       // jne restore
            code.AddRange(new byte[] { 0x8B, 0x46, 0x6C, 0x85, 0xC0 }); // eax=Owner; test eax,eax
            Skip(code, skips, 0x74);                       // je restore (no owner)
            code.AddRange(new byte[] { 0x3B, 0x05 });       // cmp eax, [CurrentPlayer]
            code.AddRange(BitConverter.GetBytes(CurrentPlayerAddress));
            Skip(code, skips, 0x75);                       // jne restore (AI / other house)
            code.AddRange(new byte[] { 0x83, 0x7E, 0x58, 0 }); // cmp Object,0
            Skip(code, skips, 0x74);                       // skip empty / special-item factory
            code.AddRange(new byte[] { 0x80, 0x7E, 0x70, 0 }); // cmp IsSuspended,0
            Skip(code, skips, 0x75);
            code.AddRange(new byte[] { 0x83, 0x7E, 0x38, 0 }); // cmp Timer.Duration,0
            Skip(code, skips, 0x7E);                       // jle restore (inactive timer)
            code.AddRange(new byte[] { 0x83, 0x7E, 0x3C, 1 }); // cmp Production.Step,1
            Skip(code, skips, 0x75);                       // preserve unsupported custom step
            code.AddRange(new byte[] { 0x83, 0x7E, 0x24, 53 }); // cmp Progress,53
            Skip(code, skips, 0x77);                       // ja restore (done or invalid)
            code.AddRange(new byte[] { 0xC7, 0x46, 0x24, 53, 0, 0, 0 });
            code.AddRange(new byte[] { 0xC7, 0x46, 0x34, 0, 0, 0, 0 });
            // Leave the final step to the original code: it checks available
            // money, charges the entire remaining Balance and marks completion.
            // Insufficient funds returns progress to 53 and sets OnHold.
            foreach (int position in skips)
            {
                int distance = code.Count - position - 1;
                if (distance > 127) throw new InvalidOperationException("Build stub branch too long.");
                code[position] = (byte)distance;
            }
            code.AddRange(new byte[] { 0x58, 0x9D });       // pop eax; popfd
            code.Add(0xE8);                                // relocated original timer CALL
            code.AddRange(BitConverter.GetBytes(unchecked(OriginalCallAddress - (codeAddress + code.Count + 4))));
            code.Add(0xE9);
            code.AddRange(BitConverter.GetBytes(unchecked(ReturnAddress - (codeAddress + code.Count + 4))));
            return code.ToArray();
        }

        private static void Skip(List<byte> code, List<int> skips, byte condition)
        {
            code.Add(condition);
            skips.Add(code.Count);
            code.Add(0);
        }
    }
}
