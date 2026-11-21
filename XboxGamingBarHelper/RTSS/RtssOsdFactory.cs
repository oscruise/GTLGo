using RTSSSharedMemoryNET;
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace XboxGamingBarHelper.RTSS
{
    /// <summary>
    /// GTLGo: crea el OSD de RTSS sin dejarle al finalizador un objeto a medio construir.
    ///
    /// <para>RTSSSharedMemoryNET.OSD es una clase C++/CLI: su constructor abre la memoria compartida
    /// de RTSS y lanza FileNotFoundException si RTSS todavia no la publico (RTSS recien abierto).
    /// Con <c>new OSD(...)</c> el objeto ya existe y queda registrado para finalizarse aunque el
    /// constructor falle; su finalizador (!OSD) vuelve a abrir la memoria compartida y, si sigue sin
    /// estar, lanza la excepcion en el hilo del finalizador. Eso paso el 21-nov 15:25:06 (RTSS abierto
    /// por GTLGo a las 15:24:51 con Zelda ya corriendo) y el manejador de excepciones del helper solto
    /// el control: el DualShock virtual quedo sin entrada hasta 15:31.</para>
    ///
    /// <para>Aqui: (1) solo se intenta si la memoria compartida ya existe con la firma de RTSS, y
    /// (2) el objeto se crea sin constructor y el constructor se llama aparte; si falla, ese objeto
    /// se saca de la cola del finalizador (GC.SuppressFinalize), que es lo que <c>new</c> no permite.</para>
    /// </summary>
    internal static class RtssOsdFactory
    {
        private const string SharedMemoryName = "RTSSSharedMemoryV2";
        private const uint FILE_MAP_READ = 0x0004;
        private const uint RtssSignature = 0x52545353; // 'RTSS'
        private const uint MinVersion = 0x00020000;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenFileMapping(uint dwDesiredAccess, bool bInheritHandle, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static readonly ConstructorInfo OsdCtor =
            typeof(OSD).GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);

        /// <summary>Intentos que fallaron y se sacaron del finalizador (para el log y las pruebas).</summary>
        internal static int FailedAttempts;

        /// <summary>
        /// true cuando RTSS ya publico su memoria compartida con la firma 'RTSS' y version 2 o mayor
        /// (lo mismo que revisa el constructor del OSD antes de usarla).
        /// </summary>
        internal static bool SharedMemoryReady()
        {
            IntPtr map = OpenFileMapping(FILE_MAP_READ, false, SharedMemoryName);
            if (map == IntPtr.Zero)
            {
                return false;
            }
            try
            {
                IntPtr view = MapViewOfFile(map, FILE_MAP_READ, 0, 0, UIntPtr.Zero);
                if (view == IntPtr.Zero)
                {
                    return false;
                }
                try
                {
                    uint signature = unchecked((uint)Marshal.ReadInt32(view, 0));
                    uint version = unchecked((uint)Marshal.ReadInt32(view, 4));
                    return signature == RtssSignature && version >= MinVersion;
                }
                finally
                {
                    UnmapViewOfFile(view);
                }
            }
            finally
            {
                CloseHandle(map);
            }
        }

        /// <summary>
        /// Crea el OSD con ese nombre, o null (con el motivo en <paramref name="error"/>) si RTSS no
        /// esta listo. Nunca deja un objeto fallido pendiente de finalizar.
        /// </summary>
        internal static OSD TryCreate(string entryName, out string error)
        {
            error = null;
            if (!SharedMemoryReady())
            {
                error = "RTSS shared memory not published yet";
                return null;
            }
            return Construct(entryName, out error);
        }

        /// <summary>
        /// El constructor del OSD sin la revision previa (las pruebas lo usan para forzar el fallo con
        /// RTSS cerrado). Si falla, el objeto se saca de la cola del finalizador.
        /// </summary>
        internal static OSD Construct(string entryName, out string error)
        {
            error = null;
            if (OsdCtor == null)
            {
                error = "OSD(string) constructor not found";
                return null;
            }
            object instance = FormatterServices.GetUninitializedObject(typeof(OSD));
            try
            {
                OsdCtor.Invoke(instance, new object[] { entryName });
                return (OSD)instance;
            }
            catch (TargetInvocationException ex)
            {
                GC.SuppressFinalize(instance);
                FailedAttempts++;
                error = (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message;
                return null;
            }
            catch (Exception ex)
            {
                GC.SuppressFinalize(instance);
                FailedAttempts++;
                error = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }
    }
}
