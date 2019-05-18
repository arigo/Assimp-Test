using System;
using System.Runtime.InteropServices;
using UnityEngine;


namespace Assimp
{
    internal static class InternalInterop
    {
        public static unsafe void WriteArray<T>(IntPtr pDest, T[] data, int startIndex, int count) where T : struct
        {
            throw new NotImplementedException();
        }

        public static unsafe void ReadArray<T>(IntPtr pSrc, T[] data, int startIndex, int count) where T : struct
        {
            Debug.Assert(startIndex == 0);   /* unclear if it's meant to be added to pSrc or data or both, but is always zero */
            int size = SizeOfInline<T>();
            for (int i = 0; i < count; i++)
                data[i] = ReadInline<T>((void *)(pSrc.ToInt64() + size * i));
        }

        public static unsafe void WriteInline<T>(void* pDest, ref T srcData) where T : struct
        {
            throw new NotImplementedException();
        }

        public static unsafe T ReadInline<T>(void* pSrc) where T : struct
        {
            return Marshal.PtrToStructure<T>((IntPtr)pSrc);
        }

        public static unsafe int SizeOfInline<T>()
        {
            return Marshal.SizeOf<T>();
        }

        public static unsafe void MemCopyInline(void* pDest, void* pSrc, int count)
        {
            byte[] tmp = new byte[count];
            Marshal.Copy((IntPtr)pSrc, tmp, 0, count);
            Marshal.Copy(tmp, 0, (IntPtr)pDest, count);
        }

        public static unsafe void MemSetInline(void* pDest, byte value, int count)
        {
            throw new NotImplementedException();
        }
    }
}
