using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MOTrainer336
{
    // Installs one verified five-byte CALL replacement while target threads are
    // stopped. Allocations ever reached by game code stay alive until game exit.
    // The enabled flag is separate from executable code; no per-frame patching.
    internal sealed class RemoteCallHook
    {
        private readonly Process process;
        private readonly IntPtr processHandle;
        private readonly int processId;
        private readonly int HookAddress;
        private readonly int ReturnAddress;
        private readonly byte[] Original;
        private readonly Func<int, int, byte[]> buildStub;
        private IntPtr allocation;
        private byte[] jump;
        private byte[] recoveryBytes;
        private bool installed;
        private bool enabled;
        private bool flagUncertain;
        private bool protectionPending;
        private uint savedProtection;

        internal bool MayBeEnabled { get { return enabled || flagUncertain; } }
        internal bool NeedsRecovery { get { return flagUncertain || protectionPending || recoveryBytes != null; } }

        internal RemoteCallHook(Process process, IntPtr processHandle, int address,
            byte[] original, Func<int, int, byte[]> buildStub)
        {
            if (process == null) throw new ArgumentNullException("process");
            if (processHandle == IntPtr.Zero) throw new ArgumentException("Invalid process handle.");
            if (original == null || original.Length != 5 || original[0] != 0xE8)
                throw new ArgumentException("Expected one complete relative CALL instruction.");
            this.process = process;
            this.processHandle = processHandle;
            processId = process.Id;
            HookAddress = address;
            ReturnAddress = address + 5;
            Original = (byte[])original.Clone();
            this.buildStub = buildStub;
        }

        internal bool SetEnabled(bool value, out string error)
        {
            error = null;
            try
            {
                if (process.HasExited)
                {
                    if (value) error = "Game process has exited.";
                    return !value;
                }
                if (processId == Process.GetCurrentProcess().Id)
                    throw new InvalidOperationException("Refusing to patch the trainer process.");
                if (value && recoveryBytes != null)
                    throw new InvalidOperationException("Hook restoration is pending; disable first.");
                if (protectionPending && value) RestoreProtection();
                if (value && !installed)
                {
                    if (!Equal(Read(HookAddress, Original.Length), Original))
                        throw new InvalidOperationException("Unsupported game code at 0x" + HookAddress.ToString("X") + "; an existing mod hook will not be overwritten.");
                    PrepareStub();
                    ReplaceCode(Original, jump);
                    installed = true;
                }
                if (allocation == IntPtr.Zero) return true;
                if (enabled != value || flagUncertain)
                {
                    // Failed writes may be partial. Never report OFF while the
                    // remote flag could still be set; retain recovery state.
                    flagUncertain = true;
                    Write(unchecked(allocation.ToInt32() + 4096), new byte[] { value ? (byte)1 : (byte)0 });
                    enabled = value;
                    flagUncertain = false;
                }
                if (protectionPending) RestoreProtection();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                if (value && allocation != IntPtr.Zero && MayBeEnabled)
                {
                    flagUncertain = true;
                    try
                    {
                        Write(unchecked(allocation.ToInt32() + 4096), new byte[] { 0 });
                        enabled = false;
                        flagUncertain = false;
                    }
                    catch (Exception rollbackError) { error += " Disable still pending: " + rollbackError.Message; }
                }
                return false;
            }
        }

        internal bool Detach(out string error)
        {
            error = null;
            try
            {
                if (process.HasExited) return true;
                if (!SetEnabled(false, out error)) return false;
                if (!installed && recoveryBytes == null) return true;
                ReplaceCode(recoveryBytes ?? jump, Original);
                installed = false;
                recoveryBytes = null;
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private void PrepareStub()
        {
            if (allocation != IntPtr.Zero) return;
            IntPtr block = Native.VirtualAllocEx(processHandle, IntPtr.Zero, new UIntPtr(8192), 0x3000, 0x04);
            if (block == IntPtr.Zero) throw Failure("Allocate game stub");
            try
            {
                int codeAddress = block.ToInt32();
                byte[] code = buildStub(codeAddress, unchecked(codeAddress + 4096));
                Write(codeAddress, code);
                Write(unchecked(codeAddress + 4096), new byte[] { 0 });
                uint oldProtect;
                if (!Native.VirtualProtectEx(processHandle, block, new UIntPtr(4096), 0x20, out oldProtect))
                    throw Failure("Protect game stub as executable/read-only");
                Flush(codeAddress, code.Length);
                byte[] branch = new byte[Original.Length];
                branch[0] = 0xE9;
                Buffer.BlockCopy(BitConverter.GetBytes(unchecked(codeAddress - ReturnAddress)), 0, branch, 1, 4);
                jump = branch;
                allocation = block;
            }
            catch
            {
                // No jump has ever targeted this block, so freeing it is safe.
                Native.VirtualFreeEx(processHandle, block, UIntPtr.Zero, 0x8000);
                throw;
            }
        }

        private void ReplaceCode(byte[] expected, byte[] replacement)
        {
            FrozenThreads frozen = new FrozenThreads(processId);
            string operationError = null;
            try
            {
                frozen.Freeze();
                if (!Equal(Read(HookAddress, expected.Length), expected))
                    throw new InvalidOperationException("Game hook bytes changed; refusing to overwrite another patch.");
                uint previousProtect;
                if (!Native.VirtualProtectEx(processHandle, new IntPtr(HookAddress), new UIntPtr((uint)expected.Length), 0x40, out previousProtect))
                    throw Failure("Make game hook writable");
                if (!protectionPending)
                {
                    savedProtection = previousProtect;
                    protectionPending = true;
                }
                try
                {
                    try
                    {
                        Write(HookAddress, replacement);
                        Flush(HookAddress, replacement.Length);
                        if (!Equal(Read(HookAddress, replacement.Length), replacement))
                            throw new InvalidOperationException("Game hook verification failed.");
                        installed = Equal(replacement, jump);
                        recoveryBytes = null;
                    }
                    catch (Exception patchError)
                    {
                        try
                        {
                            Write(HookAddress, expected);
                            Flush(HookAddress, expected.Length);
                            if (!Equal(Read(HookAddress, expected.Length), expected))
                                throw new InvalidOperationException("Game hook rollback verification failed.");
                        }
                        catch (Exception rollbackError)
                        {
                            // Retain the exact failed transaction bytes for a later
                            // ownership-checked retry; the stub remains allocated.
                            try { recoveryBytes = Read(HookAddress, expected.Length); }
                            catch { recoveryBytes = expected; }
                            throw new InvalidOperationException(patchError.Message + " Rollback failed: " + rollbackError.Message);
                        }
                        throw;
                    }
                }
                catch (Exception ex) { operationError = ex.Message; }
                finally
                {
                    try { RestoreProtection(); }
                    catch (Exception ex) { operationError = (operationError == null ? "" : operationError + " ") + ex.Message; }
                }
            }
            catch (Exception ex) { operationError = ex.Message; }
            finally
            {
                string resumeError = frozen.Resume();
                if (resumeError != null) operationError = (operationError == null ? "" : operationError + " ") + resumeError;
            }
            if (operationError != null) throw new InvalidOperationException(operationError);
        }

        private void RestoreProtection()
        {
            uint ignored;
            if (!Native.VirtualProtectEx(processHandle, new IntPtr(HookAddress), new UIntPtr((uint)Original.Length), savedProtection, out ignored))
                throw Failure("Restore game hook page protection");
            protectionPending = false;
        }

        internal byte[] Read(int address, int count)
        {
            byte[] bytes = new byte[count];
            IntPtr transferred;
            if (!Native.ReadProcessMemory(processHandle, new IntPtr(address), bytes, count, out transferred))
                throw Failure("Read game memory");
            if (transferred.ToInt64() != count) throw new InvalidOperationException("Incomplete game memory read.");
            return bytes;
        }

        internal void Write(int address, byte[] bytes)
        {
            IntPtr transferred;
            if (!Native.WriteProcessMemory(processHandle, new IntPtr(address), bytes, bytes.Length, out transferred))
                throw Failure("Write game memory");
            if (transferred.ToInt64() != bytes.Length) throw new InvalidOperationException("Incomplete game memory write.");
        }

        private void Flush(int address, int count)
        {
            if (!Native.FlushInstructionCache(processHandle, new IntPtr(address), new UIntPtr((uint)count)))
                throw Failure("Flush game instruction cache");
        }

        private static bool Equal(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
        private static Exception Failure(string action)
        {
            int code = Marshal.GetLastWin32Error();
            return new Win32Exception(code, action + ": " + new Win32Exception(code).Message + " (" + code + ")");
        }

        private sealed class FrozenThreads
        {
            private readonly int targetId;
            private readonly Dictionary<uint, IntPtr> suspended = new Dictionary<uint, IntPtr>();
            internal FrozenThreads(int targetId) { this.targetId = targetId; }
            internal void Freeze()
            {
                if (targetId == Process.GetCurrentProcess().Id)
                    throw new InvalidOperationException("Refusing to suspend the trainer process.");
                for (int pass = 0; pass < 8; pass++)
                {
                    bool added = false;
                    List<uint> ids = Enumerate();
                    if (ids.Count == 0) throw new InvalidOperationException("Game process has no live threads.");
                    foreach (uint id in ids)
                    {
                        if (suspended.ContainsKey(id)) continue;
                        IntPtr thread = Native.OpenThread(0x0042, false, id);
                        if (thread == IntPtr.Zero)
                        {
                            int code = Marshal.GetLastWin32Error();
                            if (code == 87) continue; // exited after the snapshot
                            throw new Win32Exception(code, "Open game thread failed (" + code + ").");
                        }
                        bool keep = false;
                        try
                        {
                            // A thread ID can be reused between snapshot and OpenThread.
                            uint owner = Native.GetProcessIdOfThread(thread);
                            if (owner == 0) throw Failure("Verify game thread owner");
                            if (owner != (uint)targetId) continue;
                            if (Native.SuspendThread(thread) == UInt32.MaxValue)
                            {
                                Exception failure = Failure("Suspend game thread");
                                uint exitCode;
                                if (Native.GetExitCodeThread(thread, out exitCode) && exitCode != 259) continue;
                                throw failure;
                            }
                            suspended.Add(id, thread);
                            keep = true;
                            added = true;
                        }
                        finally { if (!keep) Native.CloseHandle(thread); }
                    }
                    if (!added) return; // a second snapshot found no new threads
                }
                throw new InvalidOperationException("Game thread list did not stabilize; game hook was not changed.");
            }
            private List<uint> Enumerate()
            {
                IntPtr snapshot = Native.CreateToolhelp32Snapshot(4, 0);
                if (snapshot == new IntPtr(-1)) throw Failure("Enumerate game threads");
                try
                {
                    List<uint> result = new List<uint>();
                    Native.ThreadEntry entry = new Native.ThreadEntry();
                    entry.Size = (uint)Marshal.SizeOf(typeof(Native.ThreadEntry));
                    if (!Native.Thread32First(snapshot, ref entry)) throw Failure("Read game thread list");
                    do
                    {
                        if (entry.OwnerProcessId == (uint)targetId) result.Add(entry.ThreadId);
                        entry.Size = (uint)Marshal.SizeOf(typeof(Native.ThreadEntry));
                    } while (Native.Thread32Next(snapshot, ref entry));
                    int error = Marshal.GetLastWin32Error();
                    if (error != 18) throw new Win32Exception(error, "Read game thread list failed.");
                    return result;
                }
                finally { Native.CloseHandle(snapshot); }
            }
            internal string Resume()
            {
                string error = null;
                foreach (IntPtr thread in suspended.Values)
                {
                    try
                    {
                        if (Native.ResumeThread(thread) == UInt32.MaxValue)
                        {
                            Exception failure = Failure("Resume game thread");
                            uint exitCode;
                            if ((!Native.GetExitCodeThread(thread, out exitCode) || exitCode == 259) &&
                                Native.ResumeThread(thread) == UInt32.MaxValue)
                                error = (error == null ? "" : error + " ") + failure.Message;
                        }
                    }
                    catch (Exception ex) { error = (error == null ? "" : error + " ") + ex.Message; }
                    finally { Native.CloseHandle(thread); }
                }
                suspended.Clear();
                return error;
            }
        }

        private static class Native
        {
            [StructLayout(LayoutKind.Sequential)]
            internal struct ThreadEntry
            {
                internal uint Size, Usage, ThreadId, OwnerProcessId;
                internal int BasePriority, DeltaPriority;
                internal uint Flags;
            }
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint allocationType, uint protection);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint type);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool VirtualProtectEx(IntPtr process, IntPtr address, UIntPtr size, uint protection, out uint oldProtection);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] bytes, int size, out IntPtr transferred);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] bytes, int size, out IntPtr transferred);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Thread32First(IntPtr snapshot, ref ThreadEntry entry);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Thread32Next(IntPtr snapshot, ref ThreadEntry entry);
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint threadId);
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint GetProcessIdOfThread(IntPtr thread);
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint SuspendThread(IntPtr thread);
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint ResumeThread(IntPtr thread);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(IntPtr handle);
        }
    }
}
