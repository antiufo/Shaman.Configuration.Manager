using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shaman.Curves;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Shaman.Runtime
{
    public static class ConfigurationManager
    {
        [StaticFieldCategory(StaticFieldCategory.Stable)]
        private static HashSet<Assembly> InitializedConfigurationAssemblies = new HashSet<Assembly>();
        [StaticFieldCategory(StaticFieldCategory.Stable)]
        private static object lockobj = new object();
        [StaticFieldCategory(StaticFieldCategory.Stable)]
        private static Dictionary<string, object> Overrides;

        [StaticFieldCategory(StaticFieldCategory.Stable)]
        public static IEnumerable<string> PositionalCommandLineArgs { get; private set; }

        [StaticFieldCategory(StaticFieldCategory.Stable)]
        private static string _RepositoryDirectory;

        static ConfigurationManager()
        {
            ReadOverrides();
        }

        public static string CombineRepositoryOrEntrypointPath(string relativePath)
        {
            if (Path.IsPathRooted(relativePath)) return relativePath;
            if (RepositoryDirectory != null)
            {
                var path = Path.Combine(RepositoryDirectory, relativePath);
                if (File.Exists(path)) return path;
            }

            if (EntrypointDirectory != null)
            {
                var path = Path.Combine(EntrypointDirectory, relativePath);
                if (File.Exists(path)) return path;
            }

            throw new FileNotFoundException("Cannot determine location of " + relativePath);
        }

        public static string CombineRepositoryPath(string repositoryRelativePath)
        {
            if (Path.IsPathRooted(repositoryRelativePath)) return repositoryRelativePath;
            if (_RepositoryDirectory == null) throw new InvalidOperationException("Could not locate repository root directory.");
            return Path.Combine(RepositoryDirectory, repositoryRelativePath);
        }

        public static string RepositoryDirectory
        {
            get
            {
                return _RepositoryDirectory;
            }
        }


#if !STANDALONE
        public static bool IsShamanRepository { get; private set; }
#endif


        private static string GetLocation(Assembly asm)
        {
            return (Assembly_get_Location ?? (Assembly_get_Location = ReflectionHelper.GetWrapper<Func<Assembly, string>>(typeof(Assembly), "get_Location")))(asm);

        }

        [StaticFieldCategory(StaticFieldCategory.Stable)]
        private static Func<Assembly, string> Assembly_get_Location;

        public static string GetInformationalVersion(Assembly asm)
        {


            var fxpath = Path.GetDirectoryName(GetLocation(typeof(int).GetTypeInfo().Assembly));
            
            /*
            var path = GetLocation(asm);
            if (IsDnx.Value)
            {
                var s = path.SplitFast(Path.DirectorySeparatorChar);
                var lib = Array.LastIndexOf(s, "lib");
                if (lib != -1)
                {
                    return s[lib - 1];
                }
            }
            */
            var vers = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (vers != null) return vers.InformationalVersion;
            return null;

        }

        [StaticFieldCategory(StaticFieldCategory.Stable)]
        public static string EntrypointDirectory { get; set; }
        [StaticFieldCategory(StaticFieldCategory.Stable)]
        public static string EntrypointVersionQualifiedPackageName { get; private set; }
        [StaticFieldCategory(StaticFieldCategory.Stable)]
        public static string EntrypointAssemblyName { get; set; }
        [StaticFieldCategory(StaticFieldCategory.Stable)]
        public static string EntrypointPackageVersion { get; internal set; }
        [StaticFieldCategory(StaticFieldCategory.Stable)]
        private static string _machineName;
        public static string MachineName
        {
            get
            {
                return _machineName ?? (_machineName = Environment.GetEnvironmentVariable("ComputerName") ?? Environment.GetEnvironmentVariable("HOSTNAME"));
            }
        }

        [StaticFieldCategory(StaticFieldCategory.Configuration)]
        public static event EventHandler ConfigurationReloaded;


        public static void RefreshConfiguration()
        {
            ReadOverrides();
#if !STANDALONE
            configuration = null;
#endif
            foreach (var item in InitializedConfigurationAssemblies)
            {
                InitializeInternal(item, true);
            }
            var z = ConfigurationReloaded;
            if (z != null) z(null, EventArgs.Empty);
        }

        [StaticFieldCategory(StaticFieldCategory.Stable)]
        internal static bool IsPerformanceTest;

        [StaticFieldCategory(StaticFieldCategory.Stable)]
        private static List<string> CommandLineOverrides;

#if CORECLR
        private static string[] originalCommandLine;
        public static void Install(string[] commandLine)
        {
            if (originalCommandLine != null) throw new InvalidOperationException();
            originalCommandLine = commandLine;
        }
#endif
        private static void ReadOverrides()
        {
            CommandLineOverrides = new List<string>();
            //#if CORECLR
            //          var commandLine = originalCommandLine;
            //#else
            var commandLine = Environment.GetCommandLineArgs();
            //#endif
            var first = commandLine[0];
            var firstCommandLineArgumentIndex = 1;
            var pathFirst = Path.GetFileNameWithoutExtension(first);

            //  var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetName().Name).ToList();
            if (string.Equals(pathFirst, "PkgRun", StringComparison.OrdinalIgnoreCase) || string.Equals(pathFirst, "PkgRun.vshost", StringComparison.OrdinalIgnoreCase))
            {
                firstCommandLineArgumentIndex++;
#if CORECLR
                Sanity.NotImplemented();
#else
                EntrypointAssemblyName = (string)Assembly.GetEntryAssembly().GetType("PkgRun.Program").GetField("EntrypointAssemblyName", BindingFlags.Public | BindingFlags.Static).GetValue(null);
#endif
                EntrypointDirectory = Environment_.CurrentDirectory;
            }
            else if (string.Equals(pathFirst, "dnx", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = firstCommandLineArgumentIndex; i < commandLine.Length; i++)
                {
                    var key = commandLine[i];
                    if (key == "-p" || key == "--project")
                    {
                        var path = Path.Combine(Environment_.CurrentDirectory, commandLine[++i]);
                        if (Directory.Exists(path)) path = Path.Combine(path, "project.json");
                        if (!File.Exists(path)) throw new ArgumentException("Cannot find startup file " + path);
                        EntrypointDirectory = path;
                        EntrypointAssemblyName = Path.GetFileName(Path.GetDirectoryName(path));


                        if (EntrypointDirectory.Contains("Shaman.PkgRun"))
                        {
                            const string suffix = "-Launcher";

                            if (EntrypointAssemblyName.EndsWith(suffix))
                            {
                                EntrypointAssemblyName = EntrypointAssemblyName.Substring(0, EntrypointAssemblyName.Length - suffix.Length);
                            }
                            EntrypointDirectory = Environment_.CurrentDirectory;
                        }
                    }
                    else if (key == "--appbase" || key == "--lib" || key == "--framework" || key == "--packages" || key == "--configuration" || key == "--port")
                    {
                        var next = i != commandLine.Length - 1 ? commandLine[i + 1] : null;
                        if (next != null && !next.StartsWith("-"))
                        {
                            i++;
                        }
                    }
                    else if (!key.StartsWith("-") && !key.Contains('/') && !key.Contains('\\'))
                    {
                        //Console.WriteLine("It's command: " + key);
                        //EntrypointAssemblyName = key;
                        firstCommandLineArgumentIndex = i + 1;
                        break;
                    }
                }


            }
            else
            {
#if !CORECLR
                var systemWeb = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.GetName().Name == "System.Web");
                if (systemWeb != null)
                {
                    var hostingEnvironment = systemWeb.GetType("System.Web.Hosting.HostingEnvironment", true, false);
                    EntrypointDirectory = (string)hostingEnvironment.GetRuntimeProperty("ApplicationPhysicalPath").GetValue(null);
                }
                if (EntrypointDirectory == null)
#endif
                {
                    EntrypointDirectory = Path.GetDirectoryName(Path.GetFullPath(first));
                    EntrypointAssemblyName = Path.GetFileNameWithoutExtension(first);
                }
            }
            var positional = new List<string>();
            Overrides = new Dictionary<string, object>();

            var z = new List<string>();

            var d = EntrypointDirectory ?? Environment_.CurrentDirectory;
            z.Add(d);
            while (true)
            {
                var parent = Path.GetDirectoryName(d);
                if (parent == null || parent == d) break;
                z.Add(parent);
                d = parent;
            }
            z.Reverse();

            if (commandLine.Contains("--performance")) IsPerformanceTest = true;

            foreach (var dir in z)
            {
                if (Directory.Exists(Path.Combine(dir, ".hg")) || Directory.Exists(Path.Combine(dir, ".git")))
                {
                    _RepositoryDirectory = dir;
#if !STANDALONE
                    if (File.Exists(Path.Combine(dir, "Shaman.ApiServer.sln")))
                    {
                        IsShamanRepository = true;
                    }
#endif
                }
                LoadOverrides(Path.Combine(dir, "Configuration.json"));
            }
            foreach (var dir in z)
            {
                LoadOverrides(Path.Combine(dir, "Configuration.local.json"));
                LoadOverrides(Path.Combine(dir, "Configuration.curves.json"));
            }

            for (int i = firstCommandLineArgumentIndex; i < commandLine.Length; i++)
            {
                var key = commandLine[i];

                if (key.StartsWith("--"))
                {
                    var keyname = key.Substring(2);
                    CommandLineOverrides.Add(keyname);
                    if (commandLine.Length > i + 1 && !commandLine[i + 1].StartsWith("--"))
                    {
                        Overrides[keyname] = commandLine[i + 1];
                        i++;
                    }
                    else
                    {
                        Overrides[keyname] = true;
                    }

                }
                else
                {
                    positional.Add(key);
                }

            }
            PositionalCommandLineArgs = positional;
        }



#if DEBUG
        internal static bool IsDebugBuild = true;
#else
        internal static bool IsDebugBuild = false;
#endif

        private static void LoadOverrides(string path)
        {
            if (!File.Exists(path)) return;

            var json = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(path));

            LoadOverrides(json["properties"]);
            var debugKind = IsDebugBuild ? "debug" : "release";
            var debuggerKind = Debugger.IsAttached ? "attached" : "detached";
            LoadOverrides(json[debugKind]);
            LoadOverrides(json[debuggerKind]);
            LoadOverrides(json[debugKind + "-" + debuggerKind]);

        }

        private static void LoadOverrides(JToken token)
        {
            if (token == null) return;
            foreach (var prop in (JObject)token)
            {
                var subobj = prop.Value as JObject;
                if (subobj != null)
                {
                    foreach (var subprop in subobj)
                    {
                        Overrides[prop.Key + "." + subprop.Key] = ConvertFromJson(subprop.Value);
                    }
                }
                else
                {
                    Overrides[prop.Key] = ConvertFromJson(prop.Value);
                }



            }
        }

        private static object ConvertFromJson(JToken value)
        {
            if (value.Type == JTokenType.Null || value.Type == JTokenType.Undefined) return null;
            var jv = value as JValue;
            if (jv != null) return jv.Value;

            var arr = value as JArray;
            if (arr != null)
            {
                return arr.Select(x => ConvertFromJson(x)).ToList();
            }
            throw new NotSupportedException("Unsupported Configuration.json type.");
        }

        public static void Initialize(Assembly assembly
#if STANDALONE
        , bool debugBuild
#endif
        )

        {

#if STANDALONE
            IsDebugBuild = debugBuild;
#endif

#if CORECLR
            Sanity.NotImplemented();
#else
            EntrypointAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "Unknown";
#endif
            var coreasm = typeof(ConfigurationManager).GetTypeInfo().Assembly;
            if (assembly != coreasm)
                Initialize(coreasm
#if STANDALONE
        , debugBuild
#endif
            );
            InitializeInternal(assembly, false);
        }

        private static void InitializeInternal(Assembly assembly, bool force)
        {
            lock (lockobj)
            {
                if (!InitializedConfigurationAssemblies.Contains(assembly) || force)
                {
                    try
                    {
                        foreach (var type in assembly.GetTypes())
                        {
                            InitializeType(type);
                        }
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        var first = ex.LoaderExceptions.FirstOrDefault();
                        throw new Exception(first?.Message ?? "An ReflectionTypeLoadException exception occurred.", first);
                    }

#if !STANDALONE
                    if (assembly == typeof(ConfigurationManager).GetTypeInfo().Assembly)
                    {
                        var dict = new Dictionary<ErrorCategory, ErrorBehavior>();
                        foreach (var item in Enum.GetNames(typeof(ErrorCategory)))
                        {
                            if (IsPerformanceTest)
                            {
                                dict[(ErrorCategory)Enum.Parse(typeof(ErrorCategory), item, false)] = ErrorBehavior.LogConsole;
                                continue;
                            }
                            object arr;
                            Overrides.TryGetValue("ErrorBehavior." + item, out arr);
                            var k = ErrorBehavior.None;
                            if (Configuration_AlwaysLogToConsole) k |= ErrorBehavior.LogConsole;
                            if (Configuration_AlwaysWriteToFile) k |= ErrorBehavior.WriteToFile;
                            if (arr != null)
                            {
                                foreach (string behavstr in (IEnumerable<object>)arr)
                                {
                                    var kstr = behavstr;
                                    var negate = behavstr[0] == '-';
                                    if (negate) kstr = kstr.Substring(1);
                                    var curr = (ErrorBehavior)Enum.Parse(typeof(ErrorBehavior), kstr, false);
                                    if (negate) k &= ~curr;
                                    else k |= curr;
                                }
                            }
                            dict[(ErrorCategory)Enum.Parse(typeof(ErrorCategory), item, false)] = k;
                        }
                        dict[ErrorCategory.Fatal] |= ErrorBehavior.LogConsoleDetailed;
                        errorHandlingOptions = dict;
                    }
#endif
                    InitializedConfigurationAssemblies.Add(assembly);
#if !STANDALONE
                    Initialize(typeof(Shaman.Runtime.SingleThreadSynchronizationContext).Assembly());
#endif

                }
            }
        }

#if !STANDALONE
        [StaticFieldCategory(StaticFieldCategory.Configuration)]
        internal static Dictionary<ErrorCategory, ErrorBehavior> errorHandlingOptions = new Dictionary<ErrorCategory, ErrorBehavior>();


        [Configuration(PerformanceValue = false)]
        internal static bool Configuration_AlwaysLogToConsole;
        [Configuration(PerformanceValue = false)]
        internal static bool Configuration_AlwaysLogDetailedToConsole;

        [Configuration(PerformanceValue = false)]
        internal static bool Configuration_AlwaysWriteToFile;


        [RestrictedAccess]
        [Serializable]
        public class AssemblyConfiguration
        {
            public string AssemblyName;
            public List<TypeConfiguration> Types;
        }
        [RestrictedAccess]
        [Serializable]
        public class TypeConfiguration
        {
            public string FullName;
            public List<FieldConfiguration> Fields;
        }
        [RestrictedAccess]
        [Serializable]
        public class FieldConfiguration
        {
            public string FieldName;
            public object Value;
        }

        [StaticFieldCategory(StaticFieldCategory.Stable)]
        internal static List<AssemblyConfiguration> configuration;
#endif
        private static void InitializeType(Type type)
        {
#if !STANDALONE
            TypeConfiguration typeConfiguration = null;
#endif
            foreach (var field in type.GetRuntimeFields())
            {
                var attr = field.GetCustomAttribute<ConfigurationAttribute>();
                if (attr != null)
                {
                    object value;
                    if (TryGetValue(field, attr, out value))
                    {
                        field.SetValue(null, value);
#if !STANDALONE

                        if (typeConfiguration == null)
                        {
                            if (configuration == null) configuration = new List<AssemblyConfiguration>();
                            var asmname = type.Assembly().GetName().Name;
                            var asm = configuration.FirstOrDefault(x => x.AssemblyName == asmname);
                            if (asm == null)
                            {
                                asm = new AssemblyConfiguration() { AssemblyName = asmname, Types = new List<TypeConfiguration>() };
                                configuration.Add(asm);
                            }
                            typeConfiguration = asm.Types.FirstOrDefault(x => x.FullName == type.FullName);
                            if (typeConfiguration == null)
                            {
                                typeConfiguration = new TypeConfiguration() { FullName = type.FullName, Fields = new List<FieldConfiguration>() };
                                asm.Types.Add(typeConfiguration);
                            }
                            typeConfiguration.Fields.Add(new FieldConfiguration() { FieldName = field.Name, Value = value });

                        }
#endif
                    }
                }
            }


        }

        private static bool TryGetValue(FieldInfo field, ConfigurationAttribute attr, out object finalValue)
        {
            if (!field.IsStatic)
            {
                throw new InvalidProgramException("Field " + field.DeclaringType.FullName + "." + field.Name + " should be static because it has a [Configuration] attribute.");
            }


            var name = field.Name.StartsWith("Configuration_") ? field.Name.Substring("Configuration_".Length) : field.Name;
            var fullName = field.DeclaringType.FullName.Replace('+', '.') + '.' + name;

            if (attr.HasPerformanceValue && IsPerformanceTest && !(
                (attr.CommandLineAlias != null && CommandLineOverrides.Contains(attr.CommandLineAlias)) ||
                CommandLineOverrides.Contains(fullName)
                ))
            {
                finalValue = attr.PerformanceValue;
                return true;
            }

            object value = null;
            object overr = null;

            var hasValue = attr.CommandLineAlias != null ? Overrides.TryGetValue(attr.CommandLineAlias, out overr) : false;
            if (!hasValue)
                hasValue = Overrides.TryGetValue(fullName, out overr);

            if (hasValue)
            {
                if (typeof(IList).IsAssignableFrom(field.FieldType))
                {
                    var innerType = field.FieldType.GetInterfaces().First(x => x.GetTypeInfo().IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>)).GenericTypeArguments[0];
                    var concatenated = overr as string;

                    IEnumerable<object> items;
                    if (concatenated != null)
                    {
                        items =
                            (concatenated.Length == 0 ? new string[] { } : concatenated.SplitFast(','))
                            .Select(x => Convert.ChangeType(x, innerType, CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        items = (IEnumerable<object>)overr;
                    }
                    value = typeof(ConfigurationManager).GetMethod("ConvertArray", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethodFast(innerType).Invoke(null, new object[] { items, field });
                }
                else if (field.FieldType == typeof(Curve))
                {
                    value = Curve.Parse((string)value);
                }
                else
                {
                    value = Convert.ChangeType(overr, field.FieldType, CultureInfo.InvariantCulture);
                }
                finalValue = value;
                return true;
            }
            else
            {
                if (field.FieldType == typeof(Curve))
                {
                    finalValue = Curve.CreateNonConfigured();
                    return true;
                }
            }
            finalValue = null;
            return false;
        }

        private static object ConvertArray<T>(IEnumerable<object> items, FieldInfo field)
        {
            return field.FieldType.IsArray ? (object)items.Select(x => (T)x).ToArray() : items.Select(x => (T)x).ToList();
        }
    }
}
