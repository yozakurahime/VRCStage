"""Read-only LeapC diagnostic: service events and frame counts, no images saved."""
import ctypes as c
import collections
import json
import sys
import time

class Message(c.Structure):
    _pack_ = 1
    _fields_ = [('size', c.c_uint32), ('type', c.c_uint32), ('event', c.c_void_p), ('device', c.c_uint32)]

def main():
    dll = c.CDLL(sys.argv[1])
    dll.LeapCreateConnection.argtypes = [c.c_void_p, c.POINTER(c.c_void_p)]
    dll.LeapOpenConnection.argtypes = [c.c_void_p]
    dll.LeapPollConnection.argtypes = [c.c_void_p, c.c_uint32, c.POINTER(Message)]
    dll.LeapSetPolicyFlags.argtypes = [c.c_void_p, c.c_uint64, c.c_uint64]
    dll.LeapCloseConnection.argtypes = [c.c_void_p]
    dll.LeapDestroyConnection.argtypes = [c.c_void_p]
    handle = c.c_void_p()
    results = {'create': dll.LeapCreateConnection(None, c.byref(handle))}
    events, errors = collections.Counter(), collections.Counter()
    hands = collections.Counter()
    try:
        results['open'] = dll.LeapOpenConnection(handle)
        until = time.monotonic() + 8
        while time.monotonic() < until:
            msg = Message(c.sizeof(Message))
            result = dll.LeapPollConnection(handle, 100, c.byref(msg))
            if result:
                errors[hex(result & 0xffffffff)] += 1
                continue
            events[hex(msg.type)] += 1
            if msg.type == 1:
                results['background_policy'] = dll.LeapSetPolicyFlags(handle, 1, 0)
            if msg.type == 0x100 and msg.event:
                hands[c.c_uint32.from_address(msg.event + 32).value] += 1
    finally:
        dll.LeapCloseConnection(handle)
        dll.LeapDestroyConnection(handle)
    results.update(events=dict(events), errors=dict(errors), hands=dict(hands))
    print(json.dumps(results))

if __name__ == '__main__':
    main()
