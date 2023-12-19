using System.Text.Json;
using VestPocket.ClientServer.Core;

namespace VestPocket.ClientServer.Base
{
    public abstract class VestPocketServerOptions
    {
        public string RootUser { get; set; } = "admin";
        public string RootPassword { get; set; } = "admin";
        public string ServerPath { get; set; } = "./";
    }

    public abstract class VestPocketServer
    {
        protected bool _initialized;
        protected string? _rootUser;
        protected string? _rootPassword;
        protected string? _storagePath;
        protected string? _configPath;
        protected string? _storesIndexPath;
        protected Dictionary<string, VestPocketStore<VestPocketItem>>? _stores;
        protected Dictionary<string, VestPocketConnection>? _connections;

        public void Initialize(VestPocketServerOptions options)
        {
            if (_initialized)
            {
                throw new InvalidOperationException("The instance is already initialized.");
            }

            // The core path is the definitive root folder where all the content and 
            // metadata of the server will be located. _configPath is the direct path
            // to the configurations file and _storesIndexPath is the direct path to
            // the index file that is responsible for pointing to the server stores.
            var corePath = Path.Combine(options.ServerPath, ".vestpocket");

            _configPath = Path.Combine(corePath, "config.json");
            _storagePath = Path.Combine(options.ServerPath, ".vestpocket", "stores");
            _storesIndexPath = Path.Combine(_storagePath, "stores.json");

            var jsonOptions = JsonSerializer.Serialize(options);

            // During the initialization, the server will ensure that the configuration
            // file exists, otherwise, a configuration file will be created based on the
            // current instace configuration.
            if (!File.Exists(_configPath))
            {
                File.WriteAllText(_configPath, jsonOptions);
            }

            Directory.CreateDirectory(Path.Combine(corePath, "stores"));
            Directory.CreateDirectory(Path.Combine(corePath, "backups"));
            Directory.CreateDirectory(Path.Combine(corePath, "logs"));

            // Writes the stores.json (index file for managing stores between
            // server executions) making the same checks as previously made for the config
            // json file.
            if (!File.Exists(_storesIndexPath))
            {
                File.WriteAllText(_storesIndexPath, "{}");
            }

            // The stores loader will load all the currently persisted stores - or, if none,
            // will create an empty dictionary ready to receive new stores
            var storesIndex = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_storesIndexPath));

            _stores = storesIndex!.Select(entry =>
            {
                var options = new VestPocketOptions { FilePath = entry.Value };
                var store = new VestPocketStore<VestPocketItem>(VestPocketJsonContext.Default.VestPocketItem, options);
                return new KeyValuePair<string, VestPocketStore<VestPocketItem>>(entry.Key, store);
            }).ToDictionary();

            _connections = new();
            _rootPassword = options.RootPassword;
            _rootUser = options.RootUser;

            _initialized = true;
        }
        public void ForceClear()
        {
            var di = new DirectoryInfo(_storagePath!);

            foreach (FileInfo file in di.GetFiles())
            {
                file.Delete();
            }
            foreach (DirectoryInfo dir in di.GetDirectories())
            {
                dir.Delete(true);
            }
        }
    }
}
