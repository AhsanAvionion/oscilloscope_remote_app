using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Ivi.Visa;

namespace ScopeControl.Instrument
{
    /// <summary>
    /// GlobalResourceManager finds a vendor implementation through the IVI
    /// configuration store. When that registration is missing or was written for
    /// the other bitness, it reports "No vendor-specific VISA .NET
    /// implementation is installed" even though the DLL is sitting on disk.
    ///
    /// This loads the vendor's resource manager directly instead. Reflection
    /// keeps it out of the build, so no version numbers to chase and no extra
    /// assembly reference.
    /// </summary>
    internal static class VisaImplementationLoader
    {
        private static IResourceManager _cached;
        private static readonly List<string> Attempts = new List<string>();

        /// <summary>Everything tried on the last search, for the error message.</summary>
        public static string[] LastSearch
        {
            get { lock (Attempts) return Attempts.ToArray(); }
        }

        /// <summary>The vendor resource manager, or null if none could be loaded.</summary>
        public static IResourceManager Find()
        {
            if (_cached != null) return _cached;

            lock (Attempts)
            {
                Attempts.Clear();

                // Preferred vendors first, by assembly name (picks them out of the GAC).
                foreach (string name in new[]
                {
                    "Keysight.Visa", "Agilent.Visa",
                    "NationalInstruments.Visa", "RohdeSchwarz.RsVisa"
                })
                {
                    IResourceManager manager = FromAssemblyName(name);
                    if (manager != null) return _cached = manager;
                }

                // Then anything that looks like a VISA.NET implementation on disk.
                foreach (string file in CandidateFiles())
                {
                    IResourceManager manager = FromFile(file);
                    if (manager != null) return _cached = manager;
                }
            }
            return null;
        }

        private static IResourceManager FromAssemblyName(string simpleName)
        {
            try
            {
                Assembly assembly = Assembly.Load(simpleName);
                IResourceManager manager = FirstResourceManager(assembly);
                Attempts.Add((manager != null ? "loaded  " : "no RM   ") + simpleName);
                return manager;
            }
            catch (Exception ex)
            {
                Attempts.Add("missing " + simpleName + "  (" + ex.GetType().Name + ")");
                return null;
            }
        }

        private static IResourceManager FromFile(string path)
        {
            try
            {
                Assembly assembly = Assembly.LoadFrom(path);
                IResourceManager manager = FirstResourceManager(assembly);
                Attempts.Add((manager != null ? "loaded  " : "no RM   ") + path);
                return manager;
            }
            catch (Exception ex)
            {
                Attempts.Add("failed  " + path + "  (" + ex.GetType().Name + ")");
                return null;
            }
        }

        private static IResourceManager FirstResourceManager(Assembly assembly)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types ?? new Type[0]; }

            foreach (Type type in types)
            {
                if (type == null || type.IsAbstract || type.IsInterface) continue;
                if (!typeof(IResourceManager).IsAssignableFrom(type)) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null) continue;

                object instance = null;
                try
                {
                    instance = Activator.CreateInstance(type);

                    // Its finalizer calls into the native VISA library. If that
                    // library cannot be loaded (wrong bitness, usually) the
                    // finalizer throws on the GC thread and kills the process
                    // long after this call. We own the lifetime instead.
                    GC.SuppressFinalize(instance);
                    return (IResourceManager)instance;
                }
                catch (Exception ex)
                {
                    if (instance != null) GC.SuppressFinalize(instance);
                    Attempts.Add("rejected " + type.FullName + "  (" + ex.GetType().Name + ")");
                }
            }
            return null;
        }

        private static IEnumerable<string> CandidateFiles()
        {
            var roots = new List<string>();
            foreach (Environment.SpecialFolder folder in new[]
            {
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86
            })
            {
                string baseDir = Environment.GetFolderPath(folder);
                if (string.IsNullOrEmpty(baseDir)) continue;
                roots.Add(Path.Combine(baseDir, @"IVI Foundation\VISA\Microsoft.NET"));
                roots.Add(Path.Combine(baseDir, @"Keysight\IO Libraries Suite\bin"));
                roots.Add(Path.Combine(baseDir, @"Agilent\IO Libraries Suite\bin"));
                roots.Add(Path.Combine(baseDir, @"National Instruments\Shared\Microsoft.NET"));
            }

            var found = new List<string>();
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (string file in Directory.GetFiles(root, "*.Visa.dll", SearchOption.AllDirectories))
                    {
                        string name = Path.GetFileName(file);
                        // Ivi.Visa is the interface assembly, not an implementation.
                        if (name.Equals("Ivi.Visa.dll", StringComparison.OrdinalIgnoreCase)) continue;
                        found.Add(file);
                    }
                }
                catch (Exception ex)
                {
                    Attempts.Add("scan failed " + root + "  (" + ex.GetType().Name + ")");
                }
            }
            return found;
        }
    }
}
