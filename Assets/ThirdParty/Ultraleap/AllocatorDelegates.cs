// Copyright (C) Ultraleap, Inc. 2011-2026. Apache-2.0.
// Delegate declarations extracted unchanged from MemoryManager.cs at the revision in SOURCE.txt.
using System;
using System.Runtime.InteropServices;
namespace LeapInternal
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr Allocate(UInt32 size, eLeapAllocatorType typeHint, IntPtr state);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void Deallocate(IntPtr buffer, IntPtr state);
}
