using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MOTrainer336
{
    internal sealed class PowerOverride
    {
        private const int HookAddress = 0x508D81;
        private const int ReturnAddress = 0x508D86;
        private const int OriginalCallAddress = 0x454CE0;
        private const int CurrentPlayerAddress = 0xA83D4C;
        private const int RecheckPowerOffset = 0x5778;
        private const int RecheckRadarOffset = 0x5779;
        private static readonly byte[] Original = { 0xE8, 0x5A, 0xBF, 0xF4, 0xFF };
        private readonly Process process;
        private readonly RemoteCallHook hook;
        private bool recheckPending;
        private int lastPlayer;

        internal bool MayBeEnabled { get { return hook.MayBeEnabled; } }
        internal bool NeedsRecovery { get { return hook.NeedsRecovery || recheckPending; } }

        internal PowerOverride(Process process, IntPtr processHandle)
        {
            this.process = process;
            hook = new RemoteCallHook(process, processHandle, HookAddress, Original, BuildStub);
        }

        internal bool SetEnabled(bool value, int player, out string error)
        {
            error = null;
            try
            {
                if (process.HasExited)
                {
                    if (value) error = "Game process has exited.";
                    return !value;
                }
                if (value && !ValidPlayer(player))
                    throw new InvalidOperationException("No valid current player for unlimited power.");
                if (MayBeEnabled != value || hook.NeedsRecovery) recheckPending = true;
                if (!hook.SetEnabled(value, out error)) throw new InvalidOperationException(error);
                if (!recheckPending && (!value || player == lastPlayer)) return true;
                int current = BitConverter.ToInt32(hook.Read(CurrentPlayerAddress, 4), 0);
                if (value && current != player)
                    throw new InvalidOperationException("Current player changed; power recalculation will be retried.");
                RequestRecheck(current, value);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                if (value && (MayBeEnabled || recheckPending))
                {
                    recheckPending = true;
                    string rollbackError;
                    if (!hook.SetEnabled(false, out rollbackError)) error += " Disable still pending: " + rollbackError;
                    try { RequestRecheck(BitConverter.ToInt32(hook.Read(CurrentPlayerAddress, 4), 0), false); }
                    catch (Exception recheckError) { error += " Power recalculation still pending: " + recheckError.Message; }
                }
                return false;
            }
        }

        private void RequestRecheck(int current, bool required)
        {
            if (ValidPlayer(current))
            {
                byte[] flags = new byte[RecheckRadarOffset - RecheckPowerOffset + 1];
                flags[0] = flags[flags.Length - 1] = 1;
                hook.Write(current + RecheckPowerOffset, flags);
                lastPlayer = current;
            }
            else
            {
                if (required) throw new InvalidOperationException("No current player for power recalculation.");
                lastPlayer = 0;
            }
            recheckPending = false;
        }

        internal bool Detach(out string error)
        {
            if (!SetEnabled(false, 0, out error)) return false;
            return hook.Detach(out error);
        }

        private static bool ValidPlayer(int player) { return player >= 0x10000 && player < 0x7FFF0000; }

        internal static byte[] BuildStub(int codeAddress, int enabledAddress)
        {
            List<byte> code = new List<byte>();
            code.Add(0x9C);                                      // pushfd
            code.AddRange(new byte[] { 0x80, 0x3D });             // cmp byte ptr [enabled], 1
            code.AddRange(BitConverter.GetBytes(enabledAddress));
            code.Add(1);
            code.AddRange(new byte[] { 0x75, 0x1C });             // jne restore
            code.AddRange(new byte[] { 0x3B, 0x35 });             // cmp esi, [CurrentPlayer]
            code.AddRange(BitConverter.GetBytes(CurrentPlayerAddress));
            code.AddRange(new byte[] { 0x75, 0x14 });             // jne restore
            code.AddRange(new byte[] { 0xC7, 0x86, 0xA4, 0x53, 0, 0 });
            code.AddRange(BitConverter.GetBytes(1000000));        // PowerOutput
            code.AddRange(new byte[] { 0xC7, 0x86, 0xA8, 0x53, 0, 0, 0, 0, 0, 0 }); // PowerDrain
            code.Add(0x9D);                                      // popfd
            code.Add(0xE8);                                      // replay original CALL (relocated)
            code.AddRange(BitConverter.GetBytes(unchecked(OriginalCallAddress - (codeAddress + code.Count + 4))));
            code.Add(0xE9);
            code.AddRange(BitConverter.GetBytes(unchecked(ReturnAddress - (codeAddress + code.Count + 4))));
            return code.ToArray();
        }

    }
}
