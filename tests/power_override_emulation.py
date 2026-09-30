"""Emulate the production x86 stub and, optionally, the local game's power loop.

The JSON is emitted by PowerOverrideHarness.exe from PowerOverride.BuildStub;
this test deliberately does not reimplement the injected machine code. Game
files are read only. No game process is launched or attached. The optional game
test uses gamemd instructions with small stand-ins for building output/drain
and downstream notifications; it is not a live Ares/game integration test.
"""

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stub", required=True, type=Path)
    parser.add_argument("--deps", type=Path)
    parser.add_argument("--game", type=Path)
    args = parser.parse_args()
    if args.deps:
        sys.path.insert(0, str(args.deps))
    from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
    from unicorn.x86_const import (
        UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX,
        UC_X86_REG_ESI, UC_X86_REG_EDI, UC_X86_REG_EBP, UC_X86_REG_ESP,
        UC_X86_REG_EIP, UC_X86_REG_EFLAGS,
    )

    spec = json.loads(args.stub.read_text(encoding="utf-8-sig"))
    code = bytes.fromhex(spec["code_hex"])
    code_address = spec["code_address"]
    enabled_address = spec["enabled_address"]
    hook_address = spec["hook_address"]
    resume_address = spec["return_address"]
    assert hook_address == 0x508D81 and resume_address == 0x508D86
    original_target = 0x454CE0
    player_global = 0xA83D4C
    house_a, house_b = 0x20000000, 0x20010000
    building, vector = 0x20020000, 0x20030000
    stack, stop = 0x30008000, 0x31000000
    assertions = 0

    def check(condition, label):
        nonlocal assertions
        if not condition:
            raise AssertionError(label)
        assertions += 1

    def put32(uc, address, value):
        uc.mem_write(address, struct.pack("<I", value & 0xFFFFFFFF))

    def get32(uc, address):
        return struct.unpack("<I", uc.mem_read(address, 4))[0]

    def new_vm(game_image=None):
        uc = Uc(UC_ARCH_X86, UC_MODE_32)
        uc.mem_map(0x400000, 0x800000)
        if game_image:
            for address, data in game_image:
                uc.mem_write(address, data)
        else:
            uc.mem_write(original_target, b"\xc3")
        start = min(code_address, enabled_address) & ~0xFFF
        end = (max(code_address + len(code), enabled_address + 4) + 0xFFF) & ~0xFFF
        uc.mem_map(start, end - start)
        uc.mem_map(house_a, 0x40000)
        uc.mem_map(0x30000000, 0x10000)
        uc.mem_map(stop, 0x1000)
        uc.mem_write(code_address, code)
        uc.mem_write(hook_address, b"\xe9" + struct.pack("<i", code_address - hook_address - 5))
        return uc

    registers = [UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX,
                 UC_X86_REG_EDX, UC_X86_REG_ESI, UC_X86_REG_EDI,
                 UC_X86_REG_EBP, UC_X86_REG_ESP]

    # Execute the actual emitted stub, including its relocated original CALL.
    # Keep the stack's live portion intact; temporary PUSH space below ESP may change.
    for enabled, current_player, owner, expected in [
        (1, house_a, house_a, (1000000, 0)),
        (0, house_a, house_a, (50, 200)),
        (1, house_a, house_b, (50, 200)),
        (1, 0, house_a, (50, 200)),
        (1, house_b, house_b, (1000000, 0)),
        (1, house_b, house_a, (50, 200)),
    ]:
        for flags in [0x202, 0x203, 0x246, 0xA97, 0xED7]:
            uc = new_vm()
            put32(uc, enabled_address, enabled)
            put32(uc, player_global, current_player)
            put32(uc, owner + 0x53A4, 50)
            put32(uc, owner + 0x53A8, 200)
            state = [0x11111111, 0x22222222, owner, 0x44444444,
                     owner, 0x66666666, 0x77777777, stack]
            for register, value in zip(registers, state):
                uc.reg_write(register, value)
            uc.reg_write(UC_X86_REG_EFLAGS, flags)
            initial_flags = uc.reg_read(UC_X86_REG_EFLAGS)
            live_stack = bytes(range(128))
            uc.mem_write(stack, live_stack)
            visits = []
            uc.hook_add(UC_HOOK_CODE, lambda u, a, s, d: visits.append(a))
            uc.emu_start(hook_address, resume_address, count=200)
            label = f"enabled={enabled}, player={current_player:X}, owner={owner:X}, flags={flags:X}"
            check(uc.reg_read(UC_X86_REG_EIP) == resume_address, "returns to original continuation: " + label)
            check(tuple(get32(uc, owner + off) for off in [0x53A4, 0x53A8]) == expected,
                  "changes power only for enabled current player: " + label)
            check([uc.reg_read(r) for r in registers] == state, "preserves general registers and ESP: " + label)
            check(uc.reg_read(UC_X86_REG_EFLAGS) == initial_flags, "preserves EFLAGS: " + label)
            check(bytes(uc.mem_read(stack, len(live_stack))) == live_stack, "preserves live stack: " + label)
            check(visits.count(original_target) == 1, "replays original CALL exactly once: " + label)
    print("PASS: 30 production-stub scenarios (enable/disable, AI, no player, owner change, registers/flags/stack).")

    if args.game:
        raw = args.game.read_bytes()
        expected_hash = "7cd005d263fde203d9c84548200a057a8df61d724da3c6bd1e521eeb61cd0747"
        check(hashlib.sha256(raw).hexdigest() == expected_hash, "supported gamemd.exe fingerprint")
        pe = struct.unpack_from("<I", raw, 0x3C)[0]
        check(raw[pe:pe+4] == b"PE\0\0", "PE header")
        sections, optional_size = struct.unpack_from("<H", raw, pe+6)[0], struct.unpack_from("<H", raw, pe+20)[0]
        optional = pe + 24
        check(struct.unpack_from("<H", raw, optional)[0] == 0x10B, "32-bit game image")
        base = struct.unpack_from("<I", raw, optional+28)[0]
        check(base == 0x400000, "expected image base")
        table = optional + optional_size
        image = []
        for n in range(sections):
            section = table + n*40
            virtual, length, position = struct.unpack_from("<III", raw, section+12)
            if length:
                image.append((base + virtual, raw[position:position+length]))

        def original_bytes(address, count):
            for start, data in image:
                if start <= address and address+count <= start+len(data):
                    return data[address-start:address-start+count]
            raise AssertionError("address missing from PE")

        check(original_bytes(hook_address, 5) == bytes.fromhex("E85ABFF4FF"), "actual original hook instruction")
        check(original_bytes(original_target, 1) == b"\xc3", "original callee is RET in supported image")
        check(original_bytes(0x508C79, 6) == bytes.fromhex("889E78570000"), "power recheck clears at function start, offset 5778")
        check(original_bytes(0x508DE0, 7) == bytes.fromhex("C6867957000001"), "radar recheck set at function end, offset 5779")
        injection_file = args.game.with_name("Ares.dll.inj")
        if injection_file.exists():
            hooks = []
            for line in injection_file.read_text(encoding="utf-8-sig").splitlines():
                match = re.match(r"^([0-9A-Fa-f]+)\s*=\s*([^,]+),\s*([0-9A-Fa-f]+)", line)
                if match:
                    address, length = int(match[1], 16), int(match[3], 16)
                    hooks.append((address, length, match[2]))
            check(bool(hooks), "parsed Ares injection manifest")
            check(not any(address < hook_address+5 and address+max(length, 1) > hook_address
                          for address, length, name in hooks), "Ares manifest does not overlap chosen instruction")

        def game_vm(current_player=house_a, enabled=1):
            uc = new_vm(image)
            put32(uc, player_global, current_player)
            put32(uc, enabled_address, enabled)
            put32(uc, house_a + 0x6C, vector)
            put32(uc, house_a + 0x78, 1)
            put32(uc, vector, building)
            uc.mem_write(building+0x74, b"\x01")
            uc.mem_write(building+0x41B, b"\x01")
            uc.mem_write(house_a+0x1EC, b"\x01")
            put32(uc, house_a+0x2A4, 0xFFFFFFFF)
            put32(uc, house_a+0x2AC, 0)
            put32(uc, house_a+0x53A4, 75)
            put32(uc, house_a+0x53A8, 125)
            put32(uc, 0xA83E40, 0)  # no factories; observe their entry before skip
            return uc

        def run_power(uc, disable_at=None):
            observed = {"factory": [], "low_power": False, "changed": False, "disabled": False}
            def return_from_call(value=None):
                esp = uc.reg_read(UC_X86_REG_ESP)
                uc.reg_write(UC_X86_REG_EIP, get32(uc, esp))
                uc.reg_write(UC_X86_REG_ESP, esp+4)
                if value is not None:
                    uc.reg_write(UC_X86_REG_EAX, value)
            def on_code(u, address, size, data):
                if address == disable_at and not observed["disabled"]:
                    put32(u, enabled_address, 0)
                    u.mem_write(house_a+0x5778, b"\x01\x01")
                    observed["disabled"] = True
                if address == 0x44E7B0:
                    return_from_call(75)
                elif address == 0x44E880:
                    return_from_call(125)
                elif address == 0x70FEC0:
                    return_from_call(0)
                elif address == 0x4CA6E0:
                    observed["factory"].append((get32(u, house_a+0x53A4), get32(u, house_a+0x53A8)))
                elif address == 0x508DC2:
                    observed["low_power"] = True
                elif address == 0x50AF10:
                    observed["changed"] = True
                    return_from_call()
            handle = uc.hook_add(UC_HOOK_CODE, on_code)
            put32(uc, stack, stop)
            uc.reg_write(UC_X86_REG_ESP, stack)
            uc.reg_write(UC_X86_REG_ECX, house_a)
            uc.reg_write(UC_X86_REG_EFLAGS, 0x202)
            uc.mem_write(house_a+0x5778, b"\x01")
            uc.emu_start(0x508C30, stop, count=5000)
            uc.hook_del(handle)
            check(uc.reg_read(UC_X86_REG_EIP) == stop, "real UpdatePower returns normally")
            check(uc.mem_read(house_a+0x5779, 1) == b"\x01", "real UpdatePower requests radar update")
            return observed

        baseline = game_vm(enabled=0)
        natural = run_power(baseline)
        check(natural["low_power"], "control reaches real low-power branch")
        put32(baseline, house_a+0x53A4, 1000000)
        put32(baseline, house_a+0x53A8, 0)
        check(natural["low_power"] and get32(baseline, house_a+0x53A4) == 1000000,
              "external writes after game calculation cannot undo already-observed low-power decision")
        fixed = game_vm()
        for cycle in range(4):
            observed = run_power(fixed)
            check(not observed["low_power"], "hook prevents low-power branch during every recomputation")
            check(observed["factory"] == [(1000000, 0)], "override precedes factory build-time update")
            check(fixed.mem_read(house_a+0x5778, 1) == b"\x00", "real power recheck consumed")
        ai = game_vm(current_player=house_b)
        observed = run_power(ai)
        check(observed["low_power"] and observed["factory"] == [(75, 125)], "AI retains natural deficit")
        put32(fixed, enabled_address, 0)
        observed = run_power(fixed)
        check(observed["low_power"] and observed["factory"] == [(75, 125)], "disable recomputes building totals, not a stale snapshot")
        check(observed["changed"], "disable notifies natural power-state transition")

        # Disable at representative instruction boundaries before/after the initial
        # flag clear and override. An in-flight overridden cycle must leave a retry;
        # the next update must always recover the true building totals.
        for address in [0x508C30, 0x508C79, 0x508D81, 0x508D86, 0x508D99, 0x508DE0]:
            uc = game_vm()
            observed = run_power(uc, disable_at=address)
            check(observed["disabled"], "disable checkpoint was reached")
            if not observed["low_power"]:
                check(uc.mem_read(house_a+0x5778, 1) == b"\x01", "disable after override preserves another recheck")
            observed = run_power(uc)
            check(observed["low_power"] and observed["factory"] == [(75, 125)], "next update restores natural totals after concurrent disable")
        print("PASS: local gamemd fingerprint, hook/flag instructions, Ares manifest, real power branch/factory order, disable recovery.")
        print("Scope: gamemd machine code plus isolated callees; full Ares and live gameplay still require manual validation.")
    print(f"Completed {assertions} x86 emulation assertions; no game process was used.")


if __name__ == "__main__":
    main()
