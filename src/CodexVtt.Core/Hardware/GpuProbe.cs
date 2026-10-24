using System.Runtime.InteropServices;
using System.Text;

namespace CodexVtt.Core.Hardware;

/// <summary>
/// Scheda video trovata dal caricatore Vulkan. <c>VramBytes</c> è la memoria locale al dispositivo (heap DEVICE_LOCAL
/// più grande) e c'è solo per le schede dedicate: sulle integrate la memoria è quella condivisa del PC, quindi non si inventa un numero.
/// </summary>
public sealed record GpuDevice(string Name, string Kind, uint VendorId, long? VramBytes = null);

/// <summary>
/// Cerca le schede video utilizzabili con Vulkan (NVIDIA, AMD, Intel) senza caricare nessun motore di trascrizione:
/// serve a decidere in anticipo se provare il runtime GPU di Whisper (ADR-0009). Interroga solo il driver locale.
/// Senza driver Vulkan, o con una GPU solo software, il risultato è vuoto e si resta sulla CPU.
/// </summary>
public static class GpuProbe
{
    private static readonly Lazy<List<GpuDevice>> Cache = new(Detect, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Risultato della prima rilevazione, riusato per tutta la vita del processo.</summary>
    public static IReadOnlyList<GpuDevice> Devices => Cache.Value;

    private static List<GpuDevice> Detect()
    {
        var library = OperatingSystem.IsWindows() ? "vulkan-1.dll" : OperatingSystem.IsLinux() ? "libvulkan.so.1" : null;
        if (library is null || !NativeLibrary.TryLoad(library, out var handle))
            return [];

        try
        {
            return Enumerate(handle);
        }
#pragma warning disable CA1031 // I driver video possono fallire in molti modi: qualunque errore significa «nessuna GPU utilizzabile».
        catch (Exception ex) when (ex is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            return [];
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }

    private static List<GpuDevice> Enumerate(IntPtr library)
    {
        var createInstance = Function<VkCreateInstance>(library, "vkCreateInstance");
        var destroyInstance = Function<VkDestroyInstance>(library, "vkDestroyInstance");
        var enumerate = Function<VkEnumeratePhysicalDevices>(library, "vkEnumeratePhysicalDevices");
        var getProperties = Function<VkGetPhysicalDeviceProperties>(library, "vkGetPhysicalDeviceProperties");
        var getMemory = Function<VkGetPhysicalDeviceProperties>(library, "vkGetPhysicalDeviceMemoryProperties");

        var info = new VkInstanceCreateInfo { SType = 1 }; // VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO, il resto a zero
        if (createInstance(ref info, IntPtr.Zero, out var instance) != 0 || instance == IntPtr.Zero)
            return [];

        var found = new List<GpuDevice>();
        try
        {
            uint count = 0;
            if (enumerate(instance, ref count, null) != 0 || count == 0)
                return found;
            var devices = new IntPtr[count];
            if (enumerate(instance, ref count, devices) < 0)
                return found;

            // VkPhysicalDeviceProperties: apiVersion, driverVersion, vendorID, deviceID, deviceType (u32 ciascuno), poi deviceName[256].
            var buffer = Marshal.AllocHGlobal(4096);
            try
            {
                for (var i = 0; i < count; i++)
                {
                    getProperties(devices[i], buffer);
                    var vendor = (uint)Marshal.ReadInt32(buffer, 8);
                    var type = Marshal.ReadInt32(buffer, 16);
                    var name = Marshal.PtrToStringUTF8(buffer + 20, 256)?.Split('\0')[0].Trim() ?? "";
                    // 1 integrata, 2 dedicata, 3 virtuale; 0 «altro» e 4 «CPU» (rasterizzatori software come llvmpipe) non contano.
                    if (type is >= 1 and <= 3 && name.Length > 0)
                    {
                        long? vram = null;
                        if (type == 2)
                        {
                            getMemory(devices[i], buffer);
                            vram = LargestDeviceLocalHeap(buffer);
                        }

                        found.Add(new GpuDevice(name, type == 2 ? "dedicata" : type == 1 ? "integrata" : "virtuale", vendor, vram));
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            destroyInstance(instance, IntPtr.Zero);
        }

        return found;
    }

    /// <summary>
    /// VkPhysicalDeviceMemoryProperties: memoryTypeCount (u32), memoryTypes[32] (8 byte ciascuno), memoryHeapCount (u32 a 260),
    /// poi memoryHeaps[16] da 264 (size u64, flags u32 + padding = 16 byte). Il bit 0 dei flags è DEVICE_LOCAL.
    /// </summary>
    internal static long? LargestDeviceLocalHeap(IntPtr memoryProperties)
    {
        var heapCount = Marshal.ReadInt32(memoryProperties, 260);
        if (heapCount is < 1 or > 16)
            return null;
        long best = 0;
        for (var i = 0; i < heapCount; i++)
        {
            var size = Marshal.ReadInt64(memoryProperties, 264 + (16 * i));
            var flags = Marshal.ReadInt32(memoryProperties, 272 + (16 * i));
            if ((flags & 1) != 0 && size > best)
                best = size;
        }

        return best > 0 ? best : null;
    }

    private static T Function<T>(IntPtr library, string name)
        where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    [StructLayout(LayoutKind.Sequential)]
    private struct VkInstanceCreateInfo
    {
        public int SType;
        public IntPtr PNext;
        public uint Flags;
        public IntPtr PApplicationInfo;
        public uint EnabledLayerCount;
        public IntPtr PpEnabledLayerNames;
        public uint EnabledExtensionCount;
        public IntPtr PpEnabledExtensionNames;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkCreateInstance(ref VkInstanceCreateInfo info, IntPtr allocator, out IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkDestroyInstance(IntPtr instance, IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VkEnumeratePhysicalDevices(IntPtr instance, ref uint count, [In, Out] IntPtr[]? devices);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VkGetPhysicalDeviceProperties(IntPtr device, IntPtr properties);
}
