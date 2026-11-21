using NLog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XboxGamingBarHelper
{
    /// <summary>
    /// GTLGo: pruebas de fallas para la suite de pruebas de la Legion (LegionSetup\Pruebas). Sin el
    /// archivo no hace nada. Con LocalState\gtlgo_prueba.txt el helper lee una palabra, borra el
    /// archivo y hace esa prueba una vez:
    /// <list type="bullet">
    /// <item>excepcion_hilo: excepcion no controlada en un hilo de fondo.</item>
    /// <item>excepcion_finalizador: excepcion en el hilo del finalizador (lo que hizo el OSD de RTSS
    /// el 21-nov 15:25:06).</item>
    /// <item>salida: el helper termina con Environment.Exit(0) (prueba que al salir suelta HidHide y
    /// VIIPER y no vuelve a armarlos).</item>
    /// </list>
    /// Con las dos primeras el helper debe seguir vivo y el control intacto.
    /// </summary>
    internal static class GtlgoPruebas
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string FileName = "gtlgo_prueba.txt";

        private sealed class LanzaEnElFinalizador
        {
            ~LanzaEnElFinalizador()
            {
                throw new FileNotFoundException("GTLGo prueba: excepcion en el hilo del finalizador (como el OSD de RTSS)");
            }
        }

        internal static void Poll()
        {
            string path;
            string word;
            try
            {
                path = Path.Combine(XboxGamingBarHelper.Profile.ProfileManager.LocalFolderPath, FileName);
                if (!File.Exists(path))
                {
                    return;
                }
                word = File.ReadAllText(path).Trim().ToLowerInvariant();
                File.Delete(path);
            }
            catch (Exception ex)
            {
                Logger.Debug($"GTLGo prueba: could not read the test file: {ex.Message}");
                return;
            }

            Logger.Warn($"GTLGo prueba: '{word}'");
            switch (word)
            {
                case "excepcion_hilo":
                    var thread = new Thread(() =>
                    {
                        throw new InvalidOperationException("GTLGo prueba: excepcion no controlada en un hilo de fondo");
                    })
                    { IsBackground = true, Name = "GTLGo prueba" };
                    thread.Start();
                    break;

                case "excepcion_finalizador":
                    CreateThrowingObject();
                    Task.Run(() =>
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        Logger.Warn("GTLGo prueba: finalizers done; the helper is still running");
                    });
                    break;

                case "salida":
                    Logger.Warn("GTLGo prueba: the helper exits now (Environment.Exit)");
                    LogManager.Flush();
                    Environment.Exit(0);
                    break;

                default:
                    Logger.Warn($"GTLGo prueba: unknown test '{word}' (excepcion_hilo, excepcion_finalizador, salida)");
                    break;
            }
        }

        // Fuera de Poll para que el JIT no conserve la referencia hasta el GC.Collect.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void CreateThrowingObject()
        {
            new LanzaEnElFinalizador();
        }
    }
}
