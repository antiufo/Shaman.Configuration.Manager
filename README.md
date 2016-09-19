# Shaman.Configuration.Manager
Provides support for easily customizable configuration values.

## Usage
```csharp
using Shaman.Runtime;

namespace Shaman
{
    class Example
    {
        [Configuration]
        static int Configuration_Example1;

        [Configuration]
        static string[] Configuration_Example2 = new[] { "\r", "\n", " " };
    }
    class Program
    {
        [Configuration(CommandLineAlias = "help")]
        static bool Configuration_Help;

        static void Main()
        {
            ConfigurationManager.Initialize(typeof(Program).Assembly,
#if DEBUG
            true
#else
            false
#endif
            ); 

            if (Configuration_Help)
            {
                PrintUsage();
            }
        }
    }
}
```

## Configuration.json
```json
{
    "properties": {
        "Shaman.Example.Example1": 42,
        "Shaman.Example.Example2": ["\r", "\n", "\t", " "],
    },
    "debug": {
        // Overrides when compiled in debug mode
    },
    "attached": {
        // Overrides when debugger is attached
    },
    "detached": {
        // Overrides when debugger is not attached
    }
}
```
`Configuration.local.json` takes the precedence, if it exists.

JSON files are searched from the exe directory, progressively looking in the parent directories.
## Overriding a value via command line
`Example.exe --Shaman.Example.Example1 43`

For a more command-line friendly name, use `CommandLineAlias` (see above)
