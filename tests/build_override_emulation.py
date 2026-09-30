"""Execute the emitted build stub, optionally with the local game's Factory::Update.

Only reads gamemd.exe / Ares.dll.inj; never starts or attaches to a game. Full
factory/timer instructions run in Unicorn. The two house money interfaces use
small stand-ins (cash only, no ore storage); this is not live Ares integration.
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
    parser.add_argument('--stub', type=Path, required=True)
    parser.add_argument('--deps', type=Path)
    parser.add_argument('--game', type=Path)
    args = parser.parse_args()
    if args.deps:
        sys.path.insert(0, str(args.deps))
    from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
    from unicorn.x86_const import (UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX,
        UC_X86_REG_EDX, UC_X86_REG_ESI, UC_X86_REG_EDI, UC_X86_REG_EBP,
        UC_X86_REG_ESP, UC_X86_REG_EIP, UC_X86_REG_EFLAGS)

    spec = json.loads(args.stub.read_text(encoding='utf-8-sig'))
    code = bytes.fromhex(spec['code_hex'])
    cave, flag = spec['code_address'], spec['enabled_address']
    hook, resume = spec['hook_address'], spec['return_address']
    assert (hook, resume) == (0x4C9B68, 0x4C9B6D)
    original = 0x426630
    current = 0xA83D4C
    house, ai, factory, obj, vtable = [0x20000000 + i * 0x10000 for i in range(5)]
    stack, stop, funds = 0x30008000, 0x31000000, 0x31000100
    assertions = 0

    def check(value, label):
        nonlocal assertions
        if not value:
            raise AssertionError(label)
        assertions += 1

    def put(uc, address, value):
        uc.mem_write(address, struct.pack('<I', value & 0xFFFFFFFF))

    def get(uc, address):
        return struct.unpack('<I', uc.mem_read(address, 4))[0]

    def vm(image=None):
        uc = Uc(UC_ARCH_X86, UC_MODE_32)
        uc.mem_map(0x400000, 0x800000)
        if image:
            for address, data in image:
                uc.mem_write(address, data)
        else:
            uc.mem_write(original, b'\xc3')
        uc.mem_map(cave, 0x2000)
        uc.mem_map(house, 0x50000)
        uc.mem_map(0x30000000, 0x10000)
        uc.mem_map(stop, 0x1000)
        uc.mem_write(cave, code)
        uc.mem_write(hook, b'\xe9' + struct.pack('<i', cave - hook - 5))
        put(uc, current, house)
        put(uc, flag, 1)
        for offset, value in [(0x24, 0), (0x2C, 100), (0x34, 200), (0x38, 200),
                              (0x3C, 1), (0x58, obj), (0x60, 5000), (0x64, 5000),
                              (0x68, -1), (0x6C, house), (0x70, 0)]:
            put(uc, factory + offset, value)
        put(uc, 0xA8ED84, 100)
        return uc

    registers = [UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX,
                 UC_X86_REG_ESI, UC_X86_REG_EDI, UC_X86_REG_EBP, UC_X86_REG_ESP]
    scenarios = [
        ('enabled player', {}, 53, 0),
        ('disabled', {flag: 0}, 0, 200),
        ('AI', {factory + 0x6C: ai}, 0, 200),
        ('no player', {current: 0}, 0, 200),
        ('null owner and player', {current: 0, factory + 0x6C: 0}, 0, 200),
        ('changed player', {current: ai, factory + 0x6C: ai}, 53, 0),
        ('empty', {factory + 0x58: 0}, 0, 200),
        ('suspended', {factory + 0x70: 1}, 0, 200),
        ('stopped timer', {factory + 0x38: 0}, 0, 200),
        ('custom step', {factory + 0x3C: 2}, 0, 200),
        ('completed', {factory + 0x24: 54}, 54, 200),
        ('invalid progress', {factory + 0x24: -1}, 0xFFFFFFFF, 200),
        ('already final step', {factory + 0x24: 53}, 53, 0),
    ]
    for name, changes, progress, remaining in scenarios:
        for flags in [0x202, 0x203, 0x246, 0xA97, 0xED7]:
            uc = vm()
            for address, value in changes.items():
                put(uc, address, value)
            state = [0x11111111, 0x22222222, factory+0x2C, 0x44444444,
                     factory, factory+0x2C, 0x77777777, stack]
            for reg, value in zip(registers, state):
                uc.reg_write(reg, value)
            uc.reg_write(UC_X86_REG_EFLAGS, flags)
            initial_flags = uc.reg_read(UC_X86_REG_EFLAGS)
            live_stack = bytes(range(128))
            uc.mem_write(stack, live_stack)
            before = bytes(uc.mem_read(factory, 0x80))
            visits = []
            uc.hook_add(UC_HOOK_CODE, lambda u, a, s, d: visits.append(a))
            uc.emu_start(hook, resume, count=200)
            check(uc.reg_read(UC_X86_REG_EIP) == resume, name + ': resumes original code')
            expected = bytearray(before)
            struct.pack_into('<I', expected, 0x24, progress)
            struct.pack_into('<I', expected, 0x34, remaining)
            check(bytes(uc.mem_read(factory, 0x80)) == expected, name + ': only progress/timer change')
            check([uc.reg_read(r) for r in registers] == state, name + ': preserves registers')
            check(uc.reg_read(UC_X86_REG_EFLAGS) == initial_flags, name + ': preserves flags')
            check(bytes(uc.mem_read(stack, 128)) == live_stack, name + ': preserves stack')
            check(visits.count(original) == 1, name + ': calls original timer once')
    print('PASS: 65 build stub scenarios, including ownership, disabled/paused/empty/completed states and register preservation.')

    if args.game:
        raw = args.game.read_bytes()
        check(hashlib.sha256(raw).hexdigest() == '7cd005d263fde203d9c84548200a057a8df61d724da3c6bd1e521eeb61cd0747', 'game fingerprint')
        pe = struct.unpack_from('<I', raw, 0x3C)[0]
        section_count = struct.unpack_from('<H', raw, pe + 6)[0]
        table = pe + 24 + struct.unpack_from('<H', raw, pe + 20)[0]
        image = []
        for n in range(section_count):
            virtual, length, position = struct.unpack_from('<III', raw, table + n*40 + 12)
            if length:
                image.append((0x400000 + virtual, raw[position:position+length]))
        # vm installs a JMP, so compare the source PE's original bytes directly.
        original_bytes = next(data[hook-start:hook-start+5] for start, data in image if start <= hook < start+len(data))
        check(original_bytes == bytes.fromhex('E8C3CAF5FF'), 'actual CALL matches hook signature')
        manifest = args.game.with_name('Ares.dll.inj')
        if manifest.exists():
            entries = [re.match(r'^([0-9A-Fa-f]+)\s*=\s*([^,]+),\s*([0-9A-Fa-f]+)', line)
                       for line in manifest.read_text(encoding='utf-8-sig').splitlines()]
            entries = [m for m in entries if m]
            check(bool(entries), 'parsed Ares injection manifest')
            check(not any(int(m[1], 16) < hook+5 and int(m[1], 16)+max(int(m[3], 16), 1) > hook
                          for m in entries), 'Ares does not overlap chosen instruction')

        def game_vm(changes=None, money=10000):
            uc = vm(image)
            for owner in [house, ai]:
                put(uc, owner + 0x24, vtable)
                put(uc, owner + 0x30C, money)
            put(uc, vtable + 0x18, funds)
            # Available cash COM method (owner+0x24), and House::SpendMoney.
            uc.mem_write(funds, bytes.fromhex('8B4424048B80E8020000C20400'))
            uc.mem_write(0x4F9790, bytes.fromhex('8B44240429810C030000C20400'))
            for address, value in (changes or {}).items():
                put(uc, address, value)
            return uc

        def tick(uc):
            put(uc, stack, stop)
            uc.reg_write(UC_X86_REG_ESP, stack)
            uc.reg_write(UC_X86_REG_ECX, factory)
            uc.emu_start(0x4C9B20, stop, count=3000)
            check(uc.reg_read(UC_X86_REG_EIP) == stop, 'Factory::Update returned')
            check(uc.reg_read(UC_X86_REG_ESP) == stack+4, 'Factory::Update balanced stack')

        # Same Factory::Update runs for the six primary production categories.
        for category, slot in [('aircraft', 0x53AC), ('infantry', 0x53B0), ('vehicles', 0x53B4),
                               ('ships', 0x53B8), ('buildings', 0x53BC), ('defenses', 0x53CC)]:
            uc = game_vm({house + slot: factory})
            tick(uc)
            check(get(uc, factory+0x24) == 54, category + ': completes in one update')
            check(get(uc, factory+0x60) == 0 and get(uc, house+0x30C) == 5000, category + ': charges full balance')
            check(uc.mem_read(factory+0x70, 1) == b'\x01', category + ': normal completion state')
            check(uc.mem_read(factory+0x28, 1) == b'\x01', category + ': UI progress change flagged')
            tick(uc)
            check(get(uc, house+0x30C) == 5000, category + ': never charges completed item again')

        uc = game_vm(money=4999)
        tick(uc)
        check(get(uc, factory+0x24) == 53 and get(uc, factory+0x60) == 5000, 'insufficient funds never grants item or reduces owed balance')
        check(get(uc, house+0x30C) == 4999 and uc.mem_read(factory+0x5C, 1) == b'\x01', 'insufficient funds waits without charging')
        put(uc, house+0x30C, 5000)
        tick(uc)
        check(get(uc, factory+0x24) == 54 and get(uc, house+0x30C) == 0, 'completes immediately when full funding arrives')

        uc = game_vm({factory+0x24: 30, factory+0x60: 2100})
        tick(uc)
        check(get(uc, house+0x30C) == 7900 and get(uc, factory+0x60) == 0, 'partially paid construction charges only remaining cost')

        # Reuse the same Factory for subsequent queued objects after the game
        # has dispatched the preceding item; the override must not cache it.
        uc = game_vm()
        for cost in [5000, 1200, 450]:
            for offset, value in [(0x24, 0), (0x34, 255), (0x38, 255), (0x60, cost), (0x64, cost), (0x70, 0)]:
                put(uc, factory+offset, value)
            tick(uc)
            check(get(uc, factory+0x24) == 54 and get(uc, factory+0x60) == 0, 'successive item completes without toggling')
        check(get(uc, house+0x30C) == 3350, 'successive items charge their own exact costs')

        uc = game_vm({factory+0x70: 1})
        tick(uc)
        check(get(uc, factory+0x24) == 0, 'paused item does not advance')
        put(uc, factory+0x70, 0)
        tick(uc)
        check(get(uc, factory+0x24) == 54, 'resumed item completes in next update')

        for name, changes in [('disabled', {flag: 0}), ('AI', {factory+0x6C: ai}),
                              ('no player', {current: 0}), ('paused', {factory+0x70: 1}),
                              ('empty', {factory+0x58: 0, factory+0x38: 0})]:
            uc = game_vm(changes)
            tick(uc)
            check(get(uc, factory+0x24) == 0, name + ': no instant completion')
            check(get(uc, house+0x30C) == 10000 and get(uc, ai+0x30C) == 10000, name + ': no accelerated charge')
        print('PASS: local gamemd Factory::Update/timer, six categories, full/partial funding, funds recovery, AI, pause, and disable.')

    print(f'Completed {assertions} build emulation assertions; no live game process was used.')


if __name__ == '__main__':
    main()
