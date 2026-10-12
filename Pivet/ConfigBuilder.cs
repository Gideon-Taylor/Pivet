using Newtonsoft.Json;
using Pivet;
using Pivet.Data;
using Pivet.Data.Processors;
using Pivet.Data.Connection;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pivet
{
    class ConfigBuilder
    {
        static Config configFile;
        static string configPath;
        static ConfigBuilderConnectionManager connectionManager;

        public static string RunBuilder()
        {
            return RunBuilder("config.json");
        }

        public static string RunBuilder(string path)
        {
            configPath = path;
            connectionManager = new ConfigBuilderConnectionManager();

            if (configPath == null)
            {
                PromptWithDefault("Enter the path to the configuration file", ref configPath);
            }
            FileInfo f = new FileInfo(configPath);
            if (File.Exists(configPath))
            {
                var originalConfigPath = configPath; // Store original path for comparison
                Console.WriteLine($"Found existing configuration file: {configPath}");
                Console.WriteLine("Choose what you would like to do:");
                Console.WriteLine("  modify - Modify the existing configuration");
                Console.WriteLine("  new    - Create a new configuration (will prompt for path)");
                Console.WriteLine("  cancel - Exit without making changes");
                
                var configAction = "modify";
                PromptWithDefault("What would you like to do? (modify/new/cancel)", ref configAction);
                
                switch (configAction.ToLower())
                {
                    case "modify":
                        Console.WriteLine($"Loading existing configuration from: {configPath}");
                        if (LoadExistingConfiguration())
                        {
                            ProcessConfigQuestions();
                        }
                        else
                        {
                            return "";
                        }
                        break;
                        
                    case "new":
                        var newConfigPath = configPath.Replace(".json", "_new.json");
                        PromptWithDefault("Enter path for new configuration file", ref newConfigPath);
                        configPath = newConfigPath;
                        
                        var useExistingPath = "n";
                        if (configPath == originalConfigPath)
                        {
                            PromptWithDefault($"This will overwrite the existing file '{configPath}'. Continue? (y/n)", ref useExistingPath);
                            
                            if (useExistingPath != "y")
                            {
                                Console.WriteLine("Operation cancelled.");
                                return "";
                            }
                        }
                        
                        Console.WriteLine($"Creating new configuration at: {configPath}");
                        configFile = new Config();
                        ProcessConfigQuestions();
                        break;
                        
                    case "cancel":
                        Console.WriteLine("Configuration builder cancelled.");
                        return "";
                        
                    default:
                        Console.WriteLine("Invalid option selected. Defaulting to modify existing configuration.");
                        Console.WriteLine($"Loading existing configuration from: {configPath}");
                        if (LoadExistingConfiguration())
                        {
                            ProcessConfigQuestions();
                        }
                        else
                        {
                            return "";
                        }
                        break;
                }
            }
            else
            {
                var createNew = "y";
                PromptWithDefault("No configuration file found, would you like to create a new one? (y/n)", ref createNew);

                if (createNew == "y")
                {
                    configFile = new Config();
                    ProcessConfigQuestions();
                }

            }

            // Clean up connection manager
            connectionManager?.Dispose();
            connectionManager = null;

            if (File.Exists(configPath))
            {
                return configPath;
            } else
            {
                return "";
            }
        }

        static bool LoadExistingConfiguration()
        {
            try
            {
                string configText = File.ReadAllText(configPath);
                configFile = JsonConvert.DeserializeObject<Config>(configText);
                
                // Register all existing environments with connection manager
                foreach (var env in configFile.Environments)
                {
                    connectionManager?.RegisterEnvironment(env);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to parse configuration file. Please validate all required fields are present.");
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey();
                return false;
            }
        }

        static void ProcessConfigQuestions()
        {
            /* Check for environments, if none we need to add one before moving on to profiles */
            if (configFile.Environments.Count == 0)
            {
                Console.WriteLine("There are currently no environments defined. Making a new one.");
                AddEnvironment();
            }

            var modifyEnv = "n";
            do
            {
                PromptWithDefault("Would you like to modify an existing environment? (y/n)", ref modifyEnv);

                if (modifyEnv == "y")
                {
                    EnvironmentConfig toModify = PromptWithList("Select the environment you wish to modify", configFile.Environments);
                    ModifyEnvironment(toModify);
                }
            } while (modifyEnv == "y");

            var testEnv = "n";
            do
            {
                PromptWithDefault("Would you like to test an environment connection? (y/n)", ref testEnv);

                if (testEnv == "y")
                {
                    EnvironmentConfig toTest = PromptWithList("Select the environment connection to test", configFile.Environments);
                    TestConnection(toTest.Connection);
                }
            } while (testEnv == "y");

            var addEnv = "n";
            do
            {
                PromptWithDefault("Would you like to create a new environment? (y/n)", ref addEnv);

                if (addEnv == "y")
                {
                    AddEnvironment();
                }
            } while (addEnv == "y");


            /* Check for profiles, if none we need to add one */
            if (configFile.Profiles.Count == 0)
            {
                Console.WriteLine("There are currently no profiles defined. Making a new one.");
                AddProfile();
            }

            var modifyProfile = "n";
            do
            {
                PromptWithDefault("Would you like to modify an existing profile? (y/n)", ref modifyProfile);

                if (modifyProfile == "y")
                {
                    ProfileConfig toModify = PromptWithList("Select the profile you wish to modify", configFile.Profiles);
                    ModifyProfile(toModify);
                }
            } while (modifyProfile == "y");


            var addProfile = "n";
            do
            {
                PromptWithDefault("Would you like to create a new profile? (y/n)", ref addProfile);

                if (addProfile == "y")
                {
                    AddProfile();
                }
            } while (addProfile == "y");


            /* Check for jobs, if none we need to add one */
            if (configFile.Jobs.Count == 0)
            {
                Console.WriteLine("There are currently no jobs defined. Making a new one.");
                AddJob();
            }

            var modifyJob = "n";
            do
            {
                PromptWithDefault("Would you like to modify an existing job? (y/n)", ref modifyJob);

                if (modifyJob == "y")
                {
                    JobConfig toModify = PromptWithList("Select the job you wish to modify", configFile.Jobs);
                    ModifyJob(toModify);
                }
            } while (modifyJob == "y");

            var addJob = "n";
            do
            {
                PromptWithDefault("Would you like to create a new job? (y/n)", ref addJob);

                if (addJob == "y")
                {
                    AddJob();
                }
            } while (addJob == "y");

            // Validate configuration before saving
            ValidateConfiguration();

            SaveConfig();
        }

        private static void ModifyEnvironment(EnvironmentConfig toModify)
        {
            var name = toModify.Name;

            PromptWithDefault("Please enter a name for the environment", ref name);

            toModify.Name = name;

            ConnectionConfig conn = toModify.Connection;

            var newProvider = PromptWithEnum<ConnectionProvider>("Please select the connection type");

            conn.Provider = newProvider;

            switch (conn.Provider)
            {
                case ConnectionProvider.Bootstrap:
                    if (conn.BootstrapParameters == null)
                    {
                        conn.BootstrapParameters = new BootstrapParams();
                    }

                    PromptWithDefault("Enter the path to TNS_ADMIN", ref conn.TNS_ADMIN);
                    PromptWithDefault("Enter the TNS connection name", ref conn.TNS);
                    PromptWithDefault("Enter the schema name", ref conn.Schema);
                    PromptWithDefault("Enter the username", ref conn.BootstrapParameters.User);
                    string tempPassword = PromptForPassword("Enter the password");
                    conn.BootstrapParameters.Password = tempPassword;
                    break;
            }

            // Offer to test the connection
            var testConnection = "y";
            PromptWithDefault("Would you like to test this database connection? (y/n)", ref testConnection);
            if (testConnection == "y")
            {
                TestConnection(toModify.Connection);
            }

            SaveConfig();
            
            // Register environment with connection manager
            connectionManager?.RegisterEnvironment(toModify);
        }

        static void AddEnvironment()
        {
            EnvironmentConfig env = new EnvironmentConfig();

            ModifyEnvironment(env);

            configFile.Environments.Add(env);
            SaveConfig();
            
            // Register environment with connection manager
            connectionManager?.RegisterEnvironment(env);
        }

        static void AddProfile()
        {
            ProfileConfig profile = new ProfileConfig();

            ModifyProfile(profile);

            configFile.Profiles.Add(profile);
            SaveConfig();
        }

        static void AddJob()
        {
            JobConfig job = new JobConfig();

            ModifyJob(job);

            configFile.Jobs.Add(job);
            SaveConfig();
        }
        static List<string> FindProviders() {
            var type = typeof(IDataProcessor);
            return AppDomain.CurrentDomain.GetAssemblies().Where(a => Program.LoadedAssemblies.Contains(a)).SelectMany(s => s.GetTypes()).Where(p => type.IsAssignableFrom(p) && !p.IsInterface && !p.IsAbstract).Select(t => (Activator.CreateInstance(t) as IDataProcessor).ProcessorID).ToList();
        }
        static void ModifyProfile(ProfileConfig profile)
        {
            PromptWithDefault("Please give this profile a name", ref profile.Name);
            // PromptWithDefault("Enter the output path", ref profile.OutputFolder);
            //profile.EnvironmentName = PromptWithList("Select the environment this profile should use", configFile.Environments).Name;

            var availableProviders = FindProviders();
            profile.DataProviders = SelectMultipleFromList<string>("Select which data providers you would like", availableProviders);

            /* TODO: Builder support for RawData entries */

            ModifyFilters(profile.Filters, profile.DataProviders.Contains(new MessageCatalogProcessor().ProcessorID));

            if (profile.DataProviders.Contains(new RawDataProcessor().ProcessorID))
            {
                ConfigureRawData(profile.Filters.RawData);
            }

            //var commitByOprid = "y";
            //PromptWithDefault("Would you like commits done by OPRID where possible? (y/n)", ref commitByOprid);
            //profile.Repository.CommitByOprid = (commitByOprid == "y");

            /*var configRemoteRepo = "n";
            PromptWithDefault("Would you like to configure a remote repository? (y/n)", ref configRemoteRepo);
            if (configRemoteRepo == "y")
            {
                var repoConfig = profile.Repository;

                var repoUrl = repoConfig.Url;
                PromptWithDefault("Please enter the URL for the remote repository", ref repoUrl);
                repoConfig.Url = repoUrl;

                var username = repoConfig.User;
                PromptWithDefault("Please enter the username for pushing changes", ref username);
                repoConfig.User = username;

                var password = PromptForPassword("Please enter the password for pushing changes");
                if (!string.IsNullOrEmpty(password))
                {
                    repoConfig.Password = password;
                }

            }*/

            SaveConfig();
        }

        static void ModifyJob(JobConfig job)
        {
            PromptWithDefault("Please give this job a name", ref job.Name);
            
            PromptWithDefault("Enter the output folder path", ref job.OutputFolder);

            if (configFile.Environments.Count == 0)
            {
                Console.WriteLine("Warning: No environments defined. You must create environments before configuring jobs.");
                return;
            }

            if (configFile.Profiles.Count == 0)
            {
                Console.WriteLine("Warning: No profiles defined. You must create profiles before configuring jobs.");
                return;
            }

            // Select environment
            EnvironmentConfig selectedEnv = PromptWithList("Select the environment this job should use", configFile.Environments);
            job.EnvironmentName = selectedEnv.Name;

            // Select profile
            ProfileConfig selectedProfile = PromptWithList("Select the profile this job should use", configFile.Profiles);
            job.ProfileName = selectedProfile.Name;

            // Configure repository
            ModifyRepository(job.Repository);

            SaveConfig();
        }

        static void ConfigureRawData(List<RawDataEntry> entries)
        {
            Logger.Write("Pivet supports the concept of Raw Data, which allows for arbitrary tools tables to be included in the version control where builtin support does not exist.");
            string configureRawData = "y";
            PromptWithDefault("Would you like to configure Raw Data entries? (y/n)", ref configureRawData);

            if (configureRawData != "y")
            {
                return;
            }

            if (entries.Count > 0)
            {
                string modifyExisting = "n";
                PromptWithDefault("You have existing Raw Data entries, would you like to modify one? (y/n)", ref modifyExisting);

                while (modifyExisting == "y")
                {
                    var selectedItem = PromptWithList("Please select which existing item you want to modify", entries);

                    ModifyRawData(selectedItem);

                    PromptWithDefault("Would you like to modify another existing item? (y/n)", ref modifyExisting);
                }
            }
            string addNewEntry = (entries.Count > 0 ? "n" : "y");

            PromptWithDefault("Would you like to add a new Raw Data entry? (y/n)", ref addNewEntry);

            while (addNewEntry == "y")
            {
                RawDataEntry entry = new RawDataEntry();
                entries.Add(entry);
                ModifyRawData(entry);

                PromptWithDefault("Would you like to add a new Raw Data entry? (y/n)", ref addNewEntry);
            }
        }

        static void ModifyRawData(RawDataEntry entry)
        {
            // Try to get a database connection for validation
            string selectedEnvironment = null;
            OracleConnection dbConnection = null;
            bool hasDbConnection = TryGetDatabaseConnection(out selectedEnvironment, out dbConnection);

            Console.WriteLine("\n=== Raw Data Entry Configuration ===");
            if (hasDbConnection)
            {
                Console.WriteLine($"Using database connection: {selectedEnvironment}");
                Console.WriteLine("   Real-time validation and assistance enabled.");
            }
            else
            {
                Console.WriteLine("WARNING: No database connection available - using offline mode");
                Console.WriteLine("   Limited validation available.");
            }

            // Configure record name with validation
            ConfigureRecordName(entry, dbConnection, hasDbConnection);

            // Configure filter field with validation
            ConfigureFilterField(entry, dbConnection, hasDbConnection);

            // Configure name pattern
            var namePattern = entry.NamePattern;
            PromptWithDefault("Please enter the filename pattern for this entry (ex: {PNLNAME}.page)", ref namePattern);
            entry.NamePattern = namePattern;

            // Configure folder
            var folderName = entry.Folder;
            PromptWithDefault("Please enter the root folder name for this entry (ex: Pages)", ref folderName);
            entry.Folder = folderName;

            // Configure related tables
            var includedRelated = (entry.IncludeRelated ? "y" : "n");
            PromptWithDefault("Would you like to include related tables? (y/n)", ref includedRelated);
            entry.IncludeRelated = (includedRelated == "y");

            // Configure extra criteria with validation
            ConfigureExtraCriteria(entry, dbConnection, hasDbConnection);

            // Configure related blacklist with smart selection
            if (entry.IncludeRelated)
            {
                ConfigureRelatedTables(entry, dbConnection, hasDbConnection);
            }
            else
            {
                // Clear blacklist if not including related tables
                entry.RelatedBlacklist = new List<string>();
            }

            // Offer to test the configuration
            if (hasDbConnection)
            {
                var testConfig = "y";
                PromptWithDefault("Would you like to test this Raw Data configuration? (y/n)", ref testConfig);
                if (testConfig == "y")
                {
                    TestRawDataConfiguration(entry, dbConnection);
                }

                var previewJson = "n";
                PromptWithDefault("Would you like to preview the JSON output? (y/n)", ref previewJson);
                if (previewJson == "y")
                {
                    PreviewJsonOutput(entry, dbConnection);
                }
            }

            Console.WriteLine("\nRaw Data entry configuration completed.");
        }

        static bool TryGetDatabaseConnection(out string environmentName, out OracleConnection connection)
        {
            environmentName = null;
            connection = null;

            var registeredEnvironments = connectionManager?.GetRegisteredEnvironments();
            if (registeredEnvironments == null || registeredEnvironments.Count == 0)
            {
                Console.WriteLine("No environments available for database connection.");
                return false;
            }

            if (registeredEnvironments.Count == 1)
            {
                environmentName = registeredEnvironments[0];
            }
            else
            {
                Console.WriteLine("\n=== Select Environment for Database Validation ===");
                Console.WriteLine("Available environments:");
                for (int i = 0; i < registeredEnvironments.Count; i++)
                {
                    var info = connectionManager.GetConnectionInfo(registeredEnvironments[i]);
                    Console.WriteLine($"   {i + 1}.) {info}");
                }

                var choice = "1";
                PromptWithDefault("Select environment for database operations (or 'skip' for offline mode)", ref choice);
                
                if (choice.ToLower() == "skip" || choice.ToLower() == "s")
                {
                    return false;
                }

                if (int.TryParse(choice, out int index) && index > 0 && index <= registeredEnvironments.Count)
                {
                    environmentName = registeredEnvironments[index - 1];
                }
                else
                {
                    Console.WriteLine("Invalid selection, using offline mode.");
                    return false;
                }
            }

            var connectionResult = connectionManager.GetConnection(environmentName);
            if (connectionResult.IsSuccess)
            {
                connection = connectionResult.Connection;
                return true;
            }
            else
            {
                Console.WriteLine($"Could not connect to {environmentName}: {connectionResult.Message}");
                return false;
            }
        }

        static void ConfigureRecordName(RawDataEntry entry, OracleConnection connection, bool hasDbConnection)
        {
            while (true)
            {
                var recordName = entry.Record;
                PromptWithDefault("Please enter the record name (ex: PSPNLDEFN)", ref recordName);

                if (hasDbConnection && !string.IsNullOrWhiteSpace(recordName))
                {
                    Console.WriteLine("Validating record...");
                    var validation = RawDataDatabaseService.ValidateRecordExists(connection, recordName);
                    
                    if (validation.IsValid)
                    {
                        Console.WriteLine($"Record '{recordName}' exists with {validation.FieldCount} fields");
                        entry.Record = recordName;
                        break;
                    }
                    else
                    {
                        Console.WriteLine($"ERROR: {validation.Message}");
                        var continueAnyway = "n";
                        PromptWithDefault("Continue with this record name anyway? (y/n)", ref continueAnyway);
                        
                        if (continueAnyway == "y")
                        {
                            entry.Record = recordName;
                            break;
                        }
                        // Otherwise, loop to ask for record name again
                    }
                }
                else
                {
                    entry.Record = recordName;
                    break;
                }
            }
        }

        static void ConfigureFilterField(RawDataEntry entry, OracleConnection connection, bool hasDbConnection)
        {
            if (!hasDbConnection)
            {
                var offlineFilterField = entry.FilterField;
                PromptWithDefault("Please enter the field name for item prefix filtering (ex: PNLNAME)", ref offlineFilterField);
                entry.FilterField = offlineFilterField;
                return;
            }

            // Get available fields for the record
            var fields = RawDataDatabaseService.GetRecordFields(connection, entry.Record);
            
            if (fields.Count == 0)
            {
                Console.WriteLine("Could not retrieve field information for record.");
                var fallbackFilterField = entry.FilterField;
                PromptWithDefault("Please enter the field name for item prefix filtering (ex: PNLNAME)", ref fallbackFilterField);
                entry.FilterField = fallbackFilterField;
                return;
            }

            Console.WriteLine($"\nAvailable fields in {entry.Record}:");
            Console.WriteLine("Common filter fields (character fields suitable for prefix filtering):");
            
            var charFields = fields.Where(f => f.FieldType == 0 && f.Length > 1) // Character fields
                                  .ToList();
            
            for (int i = 0; i < charFields.Count && i < 10; i++) // Show up to 10
            {
                var keyIndicator = charFields[i].IsKey ? " (Key)" : "";
                Console.WriteLine($"   {i + 1}.) {charFields[i].Name}{keyIndicator} - Length: {charFields[i].Length}");
            }

            if (charFields.Count > 10)
            {
                Console.WriteLine($"   ... and {charFields.Count - 10} more");
            }

            var filterField = entry.FilterField;
            PromptWithDefault("Enter field name for prefix filtering (or select number above)", ref filterField);

            // Check if they selected a number
            if (int.TryParse(filterField, out int selectedIndex) && selectedIndex > 0 && selectedIndex <= Math.Min(10, charFields.Count))
            {
                filterField = charFields[selectedIndex - 1].Name;
                Console.WriteLine($"Selected: {filterField}");
            }

            // Validate the field
            if (!string.IsNullOrWhiteSpace(filterField))
            {
                var validation = RawDataDatabaseService.ValidateFilterField(connection, entry.Record, filterField);
                if (validation.IsValid)
                {
                    if (validation.FieldType != 0) // Not character field
                    {
                        Console.WriteLine($"WARNING: {filterField} is not a character field - prefix filtering may not work as expected");
                    }
                    else
                    {
                        Console.WriteLine($"Filter field '{filterField}' validated");
                    }
                }
                else
                {
                    Console.WriteLine($"ERROR: {validation.Message}");
                }
            }

            entry.FilterField = filterField;
        }

        static void ConfigureExtraCriteria(RawDataEntry entry, OracleConnection connection, bool hasDbConnection)
        {
            var extraCriteria = entry.ExtraCriteria ?? "";
            PromptWithDefault("Enter any extra SQL WHERE criteria (leave blank for none)", ref extraCriteria);

            if (hasDbConnection && !string.IsNullOrWhiteSpace(extraCriteria))
            {
                Console.WriteLine("Testing SQL criteria...");
                var testResult = TestSQLCriteria(connection, entry.Record, extraCriteria);
                
                if (testResult.IsSuccess)
                {
                    Console.WriteLine($"SQL criteria is valid (would return {testResult.RowCount} rows)");
                }
                else
                {
                    Console.WriteLine($"ERROR: SQL criteria error: {testResult.Message}");
                    var continueAnyway = "n";
                    PromptWithDefault("Continue with this criteria anyway? (y/n)", ref continueAnyway);
                    
                    if (continueAnyway != "y")
                    {
                        extraCriteria = "";
                    }
                }
            }

            entry.ExtraCriteria = string.IsNullOrWhiteSpace(extraCriteria) ? null : extraCriteria;
        }

        static void ConfigureRelatedTables(RawDataEntry entry, OracleConnection connection, bool hasDbConnection)
        {
            if (entry.RelatedBlacklist == null)
            {
                entry.RelatedBlacklist = new List<string>();
            }

            if (!hasDbConnection)
            {
                Console.WriteLine("Database connection not available - using text-based blacklist management.");
                ManageRelatedBlacklistFallback(entry.RelatedBlacklist);
                return;
            }

            Console.WriteLine("\nDiscovering related tables...");
            var relatedResult = RawDataDatabaseService.FindRelatedTables(connection, entry.Record, null);
            
            if (!relatedResult.IsSuccess)
            {
                Console.WriteLine($"ERROR: {relatedResult.Message}");
                ManageRelatedBlacklistFallback(entry.RelatedBlacklist);
                return;
            }

            if (relatedResult.RelatedTables.Count == 0)
            {
                Console.WriteLine("No related tables found for this record.");
                return;
            }

            Console.WriteLine($"Found {relatedResult.RelatedTables.Count} related tables:");
            for (int i = 0; i < relatedResult.RelatedTables.Count; i++)
            {
                Console.WriteLine($"   {i + 1}.) {relatedResult.RelatedTables[i]}");
            }

            var configureInclusions = "y";
            PromptWithDefault("Would you like to select which tables to include? (y/n)", ref configureInclusions);
            
            if (configureInclusions == "y")
            {
                Console.WriteLine("\nSelect tables to INCLUDE (comma-separated numbers, or 'all' for all tables):");
                var inclusions = "";
                PromptWithDefault("Tables to include", ref inclusions);

                if (inclusions.ToLower() == "all")
                {
                    // Include all tables (clear blacklist)
                    entry.RelatedBlacklist.Clear();
                    Console.WriteLine("   Including all related tables");
                }
                else if (!string.IsNullOrWhiteSpace(inclusions))
                {
                    var includeNumbers = inclusions.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => int.TryParse(s, out _))
                        .Select(int.Parse)
                        .Where(n => n > 0 && n <= relatedResult.RelatedTables.Count)
                        .ToList();

                    // Build blacklist from all tables EXCEPT the included ones
                    entry.RelatedBlacklist.Clear();
                    for (int i = 0; i < relatedResult.RelatedTables.Count; i++)
                    {
                        if (!includeNumbers.Contains(i + 1))
                        {
                            var tableName = relatedResult.RelatedTables[i];
                            entry.RelatedBlacklist.Add(tableName);
                        }
                    }

                    Console.WriteLine($"   Including {includeNumbers.Count} tables, excluding {entry.RelatedBlacklist.Count} tables");
                }
            }
            else
            {
                // User doesn't want to configure - exclude all tables
                entry.RelatedBlacklist.Clear();
                entry.RelatedBlacklist.AddRange(relatedResult.RelatedTables);
                Console.WriteLine("   Excluding all related tables");
            }

            // Show current included/excluded tables
            if (relatedResult.RelatedTables.Count > 0)
            {
                var includedTables = relatedResult.RelatedTables.Where(t => !entry.RelatedBlacklist.Contains(t)).ToList();
                
                if (includedTables.Count > 0)
                {
                    Console.WriteLine($"\nCurrently included tables: {string.Join(", ", includedTables)}");
                }
                if (entry.RelatedBlacklist.Count > 0)
                {
                    Console.WriteLine($"Currently excluded tables: {string.Join(", ", entry.RelatedBlacklist)}");
                }
                
                var modifySelection = "n";
                PromptWithDefault("Would you like to modify the table selection? (y/n)", ref modifySelection);
                
                if (modifySelection == "y")
                {
                    ManageRelatedIncludes(entry.RelatedBlacklist, relatedResult.RelatedTables);
                }
            }
        }

        static void TestRawDataConfiguration(RawDataEntry entry, OracleConnection connection)
        {
            Console.WriteLine("\n🧪 Testing Raw Data Configuration...");
            
            // We need the current prefixes for testing
            var prefixes = new List<string>(); // In a real scenario, this would come from current filters
            
            var testResult = RawDataDatabaseService.TestRawDataEntry(connection, entry, prefixes);
            
            Console.WriteLine($"Record Exists: {(testResult.RecordExists ? "[YES]" : "[NO]")}");
            if (testResult.RecordExists)
            {
                Console.WriteLine($"   Field Count: {testResult.FieldCount}");
            }

            if (!string.IsNullOrWhiteSpace(entry.FilterField))
            {
                Console.WriteLine($"Filter Field Valid: {(testResult.FilterFieldExists ? "[YES]" : "[WARNING]")}");
            }

            if (entry.IncludeRelated)
            {
                Console.WriteLine($"Related Tables: {(testResult.RelatedTablesFound ? "[YES]" : "[WARNING]")} ({testResult.RelatedTableCount} found)");
                if (testResult.KeyFields.Count > 0)
                {
                    Console.WriteLine($"   Key Fields: {string.Join(", ", testResult.KeyFields)}");
                }
            }

            Console.WriteLine($"Sample Data Retrieved: {(testResult.SampleDataRetrieved ? "[YES]" : "[NO]")}");
            if (testResult.SampleDataRetrieved)
            {
                Console.WriteLine($"   Sample Rows: {testResult.SampleRowCount}");
            }

            if (testResult.ErrorMessages.Count > 0)
            {
                Console.WriteLine("\nERRORS:");
                foreach (var error in testResult.ErrorMessages)
                {
                    Console.WriteLine($"   • {error}");
                }
            }

            if (testResult.WarningMessages.Count > 0)
            {
                Console.WriteLine("\nWARNINGS:");
                foreach (var warning in testResult.WarningMessages)
                {
                    Console.WriteLine($"   • {warning}");
                }
            }

            Console.WriteLine($"\nOverall: {(testResult.IsValid ? "VALID" : "INVALID")}");
        }

        static void PreviewJsonOutput(RawDataEntry entry, OracleConnection connection)
        {
            Console.WriteLine("\n📄 Generating JSON Preview...");
            
            var prefixes = new List<string>(); // In a real scenario, this would come from current filters
            var previewResult = RawDataDatabaseService.GenerateJSONPreview(connection, entry, prefixes);
            
            if (previewResult.IsSuccess)
            {
                Console.WriteLine("JSON Preview (sample record):");
                Console.WriteLine("═══════════════════════════════════════");
                Console.WriteLine(previewResult.JsonPreview);
                Console.WriteLine("═══════════════════════════════════════");
            }
            else
            {
                Console.WriteLine($"ERROR: Could not generate preview: {previewResult.Message}");
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        static RowCountResult TestSQLCriteria(OracleConnection connection, string recordName, string extraCriteria)
        {
            try
            {
                var tableName = RawDataDatabaseService.GetTableSQLName(connection, recordName);
                var query = $"SELECT COUNT(*) FROM {tableName} WHERE {extraCriteria}";
                
                using (var cmd = new OracleCommand(query, connection))
                {
                    var count = Convert.ToInt32(cmd.ExecuteScalar());
                    return new RowCountResult(true, "SQL criteria is valid", count);
                }
            }
            catch (Exception ex)
            {
                return new RowCountResult(false, ex.Message, 0);
            }
        }

        static void ManageRelatedIncludes(List<string> blacklist, List<string> allRelatedTables)
        {
            var includedTables = allRelatedTables.Where(t => !blacklist.Contains(t)).ToList();
            
            Console.WriteLine("\nAll related tables:");
            for (int i = 0; i < allRelatedTables.Count; i++)
            {
                var status = blacklist.Contains(allRelatedTables[i]) ? "(Excluded)" : "(Included)";
                Console.WriteLine($"   {i + 1}.) {allRelatedTables[i]} {status}");
            }

            Console.WriteLine("\nSelect tables to INCLUDE (comma-separated numbers, or 'all' for all tables, or 'none' for no tables):");
            var inclusions = "";
            PromptWithDefault("Tables to include", ref inclusions);

            if (inclusions.ToLower() == "all")
            {
                blacklist.Clear();
                Console.WriteLine("Including all related tables");
            }
            else if (inclusions.ToLower() == "none")
            {
                blacklist.Clear();
                blacklist.AddRange(allRelatedTables);
                Console.WriteLine("Excluding all related tables");
            }
            else if (!string.IsNullOrWhiteSpace(inclusions))
            {
                var includeNumbers = inclusions.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => int.TryParse(s, out _))
                    .Select(int.Parse)
                    .Where(n => n > 0 && n <= allRelatedTables.Count)
                    .ToList();

                // Rebuild blacklist from all tables EXCEPT the included ones
                blacklist.Clear();
                for (int i = 0; i < allRelatedTables.Count; i++)
                {
                    if (!includeNumbers.Contains(i + 1))
                    {
                        blacklist.Add(allRelatedTables[i]);
                    }
                }

                var newIncludedCount = allRelatedTables.Count - blacklist.Count;
                Console.WriteLine($"Including {newIncludedCount} tables, excluding {blacklist.Count} tables");
            }
        }

        static void ManageRelatedBlacklistFallback(List<string> blacklist)
        {
            string configureBlacklist = "n";
            PromptWithDefault("Would you like to configure related table blacklist (tables to exclude)? (y/n)", ref configureBlacklist);
            
            if (configureBlacklist != "y")
            {
                return;
            }

            if (blacklist.Count > 0)
            {
                Console.WriteLine("Current blacklisted tables:");
                for (int i = 0; i < blacklist.Count; i++)
                {
                    Console.WriteLine($"   {i + 1}.) {blacklist[i]}");
                }

                string removeItems = "n";
                PromptWithDefault("Would you like to remove any blacklisted tables? (y/n)", ref removeItems);
                
                while (removeItems == "y")
                {
                    var indexToRemove = "1";
                    PromptWithDefault("Which table would you like to remove? (enter number)", ref indexToRemove);
                    
                    if (int.TryParse(indexToRemove, out int index) && index > 0 && index <= blacklist.Count)
                    {
                        string removedTable = blacklist[index - 1];
                        blacklist.RemoveAt(index - 1);
                        Console.WriteLine($"Removed '{removedTable}' from blacklist.");
                    }
                    else
                    {
                        Console.WriteLine("Invalid selection.");
                    }

                    if (blacklist.Count > 0)
                    {
                        PromptWithDefault("Would you like to remove another table? (y/n)", ref removeItems);
                    }
                    else
                    {
                        removeItems = "n";
                    }
                }
            }

            string addItems = "y";
            PromptWithDefault("Would you like to add tables to the blacklist? (y/n)", ref addItems);
            
            while (addItems == "y")
            {
                string tableName = "";
                PromptWithDefault("Enter the table name to blacklist", ref tableName);
                
                if (!string.IsNullOrWhiteSpace(tableName) && !blacklist.Contains(tableName))
                {
                    blacklist.Add(tableName);
                    Console.WriteLine($"Added '{tableName}' to blacklist.");
                }
                else if (blacklist.Contains(tableName))
                {
                    Console.WriteLine($"Table '{tableName}' is already in the blacklist.");
                }

                PromptWithDefault("Would you like to add another table to blacklist? (y/n)", ref addItems);
            }
        }

        static void ModifyRepository(RepositoryConfig repo)
        {
            var configureRepo = "n";
            PromptWithDefault("Would you like to configure remote repository settings? (y/n)", ref configureRepo);

            if (configureRepo != "y")
            {
                return;
            }

            PromptWithDefault("Enter the remote repository URL", ref repo.Url);

            var branch = repo.Branch ?? "";
            PromptWithDefault("Enter the branch to push to, so jobs can share a repository (blank for master)", ref branch);
            repo.Branch = string.IsNullOrWhiteSpace(branch) ? null : branch.Trim();

            PromptWithDefault("Enter the git username", ref repo.User);
            
            string tempPassword = PromptForPassword("Enter the git password/token");
            if (!string.IsNullOrEmpty(tempPassword))
            {
                repo.Password = tempPassword;
            }

            var commitByOprid = (repo.CommitByOprid ? "y" : "n");
            PromptWithDefault("Would you like commits done by OPRID where possible? (y/n)", ref commitByOprid);
            repo.CommitByOprid = (commitByOprid == "y");

            var selectedCommitStyle = PromptWithEnum<CommitStyleOptions>("Select the commit style");
            repo.CommitStyle = selectedCommitStyle;
        }

        static void ModifyFilters(FilterConfig filters, bool hasMsgCat)
        {
            filters.Projects = GetMultipleStrings("Please enter any project prefixes that should be used", String.Join(",", filters.Projects));
            filters.Prefixes = GetMultipleStrings("Please enter any item prefixes that should be used", String.Join(",", filters.Prefixes));

            if (hasMsgCat)
            {
                if (filters.MessageCatalogs == null)
                {
                    filters.MessageCatalogs = new List<MessageCatalogFilter>();
                }
                if (filters.MessageCatalogs.Count == 0)
                {
                    /* Would you like to add a message catalog range? */
                    var addMsgCat = "y";
                    PromptWithDefault("Would you like to add a message catalog range? (y/n)", ref addMsgCat);
                    if (addMsgCat == "y")
                    {
                        var addAnother = "y";
                        while (addAnother == "y")
                        {
                            AddMessageCatalog(filters.MessageCatalogs);
                            PromptWithDefault("Would you like to add another message catalog range? (y/n)", ref addAnother);
                        }
                    }

                } else
                {
                    var modifyMsgCat = "y";
                    PromptWithDefault("Would you like to modify an existing message catalog range? (y/n)", ref modifyMsgCat);
                    if (modifyMsgCat == "y")
                    {
                        var editAnother = "y";
                        while (editAnother == "y")
                        {
                            Console.WriteLine("Here are the existing message catalog ranges: ");
                            var x = 1;
                            foreach (var item in filters.MessageCatalogs)
                            {
                                Console.WriteLine($"   {x++}.) {item.Set} {item.Min}-{item.Max}");
                            }

                            var indexToEdit = "1";
                            PromptWithDefault("Which set would you like to modify?", ref indexToEdit);

                            ModifyMessageCatalog(filters.MessageCatalogs[int.Parse(indexToEdit) - 1]);

                            PromptWithDefault("Would you like to modify another message catalog range? (y/n)", ref editAnother);
                        }
                    }

                    var addMsgCat = "y";
                    PromptWithDefault("Would you like to add a message catalog range? (y/n)", ref addMsgCat);
                    if (addMsgCat == "y")
                    {
                        var addAnother = "y";
                        while (addAnother == "y")
                        {
                            AddMessageCatalog(filters.MessageCatalogs);
                            PromptWithDefault("Would you like to add another message catalog range? (y/n)", ref addAnother);
                        }
                    }
                }
            }
            else
            {
                filters.MessageCatalogs = null;
            }

        }
        static void AddMessageCatalog(List<MessageCatalogFilter> msgCats)
        {
            MessageCatalogFilter filter = new MessageCatalogFilter();
            ModifyMessageCatalog(filter);
            msgCats.Add(filter);
        }
        static void ModifyMessageCatalog(MessageCatalogFilter msgCat)
        {
            var newSet = msgCat.Set.ToString();
            var newMin = msgCat.Min.ToString();
            var newMax = msgCat.Max.ToString();

            PromptWithDefault("Please enter the message set:", ref newSet);
            PromptWithDefault("Please enter the minimum message number:", ref newMin);
            PromptWithDefault("Please enter the maximum message number:", ref newMax);

            msgCat.Set = int.Parse(newSet);
            msgCat.Min = int.Parse(newMin);
            msgCat.Max = int.Parse(newMax);
        }

        static List<string> GetMultipleStrings(string msg, string defValue)
        {
            var choice = defValue;
            PromptWithDefault(msg, ref choice);

            var choices = choice.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());

            return choices.ToList();
        }

        static List<T> SelectMultipleFromList<T>(string msg, List<T> items)
        {

            Console.WriteLine($"{msg}: ");
            var x = 1;
            foreach (T item in items)
            {
                Console.WriteLine($"   {x++}.) {item.ToString()}");
            }
            Console.WriteLine($"   {items.Count + 1}.) All");
            var choice = $"{items.Count + 1}";
            PromptWithDefault("Select one or more Data Providers (comma separated)", ref choice);

            var choiceIndexes = choice.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => int.Parse(s.Trim())).ToList();
            var valueList = new List<T>();
            if ($"{items.Count+1}" == choice)
            {
                return items;
            }

            foreach (var index in choiceIndexes)
            {
                valueList.Add((T)items[index - 1]);
            }

            return valueList;
        }

        static List<T> SelectMultipleWithEnum<T>(string msg)
        {
            var enumValues = Enum.GetValues(typeof(T));

            Console.WriteLine($"{msg}: ");
            var x = 1;
            foreach (T item in enumValues)
            {
                Console.WriteLine($"   {x++}.) {item.ToString()}");
            }

            var choice = "";
            PromptWithDefault("Select one or more Data Providers (comma separated)", ref choice);

            var choiceIndexes = choice.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => int.Parse(s.Trim()));
            var valueList = new List<T>();
            foreach (var index in choiceIndexes)
            {
                valueList.Add((T)enumValues.GetValue(index - 1));
            }

            return valueList;
        }

        static T PromptWithList<T>(string msg, List<T> items)
        {
            Console.WriteLine($"{msg}: ");
            var x = 1;
            foreach (T item in items)
            {
                Console.WriteLine($"   {x++}.) {item.ToString()}");
            }

            var choice = "1";
            PromptWithDefault("Select a choice", ref choice);

            var choiceInt = int.Parse(choice);
            return items[choiceInt - 1];
        }

        static T PromptWithEnum<T>(string msg) where T : struct
        {
            Console.WriteLine($"{msg}: ");
            var x = 1;
            foreach (T item in Enum.GetValues(typeof(T)))
            {
                Console.WriteLine($"   {x}.) {item.ToString()}");
            }

            var choice = "1";
            PromptWithDefault("Select a choice", ref choice);

            var choiceInt = int.Parse(choice);
            return (T)Enum.GetValues(typeof(T)).GetValue(choiceInt - 1);
        }

        static string Prompt(string promptMessage)
        {
            Console.Write($"{promptMessage}: ");
            return Console.ReadLine();
        }

        static void PromptWithDefault(string promptMessage, ref string defaultValue)
        {
            if (defaultValue.Length > 0)
            {
                Console.Write($"{promptMessage} [{defaultValue}]: ");
            }
            else
            {
                Console.Write($"{promptMessage}: ");
            }
            var line = Console.ReadLine();

            if (line != "")
            {
                defaultValue = line;
            }
        }

        static string PromptForPassword(string promptMessage)
        {
            Console.Write($"{promptMessage}: ");
            return Program.ReadPassword('*');
        }

        static void SaveConfig()
        {
            var configText = JsonConvert.SerializeObject(configFile, Formatting.Indented);
            File.WriteAllText(configPath, configText);
        }

        static void ValidateConfiguration()
        {
            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();

            // Validate environments
            if (configFile.Environments.Count == 0)
            {
                errors.Add("No environments defined");
            }
            else
            {
                foreach (var env in configFile.Environments)
                {
                    if (string.IsNullOrWhiteSpace(env.Name))
                        errors.Add($"Environment has no name");
                    if (string.IsNullOrWhiteSpace(env.Connection.TNS))
                        errors.Add($"Environment '{env.Name}' has no TNS connection name");
                    if (env.Connection.BootstrapParameters == null || string.IsNullOrWhiteSpace(env.Connection.BootstrapParameters.User))
                        errors.Add($"Environment '{env.Name}' has no bootstrap user");
                }
            }

            // Validate profiles
            if (configFile.Profiles.Count == 0)
            {
                errors.Add("No profiles defined");
            }
            else
            {
                foreach (var profile in configFile.Profiles)
                {
                    if (string.IsNullOrWhiteSpace(profile.Name))
                        errors.Add($"Profile has no name");
                    if (profile.DataProviders.Count == 0)
                        warnings.Add($"Profile '{profile.Name}' has no data providers");
                }
            }

            // Validate jobs
            if (configFile.Jobs.Count == 0)
            {
                errors.Add("No jobs defined");
            }
            else
            {
                foreach (var job in configFile.Jobs)
                {
                    if (string.IsNullOrWhiteSpace(job.Name))
                        errors.Add($"Job has no name");
                    if (string.IsNullOrWhiteSpace(job.OutputFolder))
                        errors.Add($"Job '{job.Name}' has no output folder");
                    if (string.IsNullOrWhiteSpace(job.EnvironmentName))
                        errors.Add($"Job '{job.Name}' has no environment specified");
                    else if (!configFile.Environments.Any(e => e.Name == job.EnvironmentName))
                        errors.Add($"Job '{job.Name}' references non-existent environment '{job.EnvironmentName}'");
                    if (string.IsNullOrWhiteSpace(job.ProfileName))
                        errors.Add($"Job '{job.Name}' has no profile specified");
                    else if (!configFile.Profiles.Any(p => p.Name == job.ProfileName))
                        errors.Add($"Job '{job.Name}' references non-existent profile '{job.ProfileName}'");
                }
            }

            // Display validation results
            if (errors.Count > 0)
            {
                Console.WriteLine("\n*** CONFIGURATION ERRORS ***");
                foreach (var error in errors)
                {
                    Console.WriteLine($"ERROR: {error}");
                }
            }

            if (warnings.Count > 0)
            {
                Console.WriteLine("\n*** CONFIGURATION WARNINGS ***");
                foreach (var warning in warnings)
                {
                    Console.WriteLine($"WARNING: {warning}");
                }
            }

            if (errors.Count == 0 && warnings.Count == 0)
            {
                Console.WriteLine("\nConfiguration validation passed!");
            }
            else if (errors.Count == 0)
            {
                Console.WriteLine($"\nConfiguration is valid but has {warnings.Count} warning(s).");
            }
            else
            {
                Console.WriteLine($"\nConfiguration has {errors.Count} error(s) and {warnings.Count} warning(s).");
                Console.WriteLine("Please review and correct the configuration.");
            }

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        static void TestConnection(ConnectionConfig connectionConfig)
        {
            Console.WriteLine("\n=== Testing Database Connection ===");
            
            try
            {
                // Create the appropriate connection provider based on the provider type
                var connectionProvider = connectionConfig.Provider;
                var providerType = Type.GetType("Pivet.Data.Connection." + connectionProvider + "Connection");
                
                if (providerType == null)
                {
                    Console.WriteLine("ERROR: Unable to find the specified database provider: " + connectionProvider);
                    return;
                }

                var dbProvider = Activator.CreateInstance(providerType) as IConnectionProvider;
                dbProvider.SetParameters(connectionConfig);
                
                Console.WriteLine($"Testing connection to: {connectionConfig.TNS}");
                Console.WriteLine($"   User: {connectionConfig.BootstrapParameters.User}");
                Console.WriteLine($"   TNS Admin: {connectionConfig.TNS_ADMIN}");
                if (!string.IsNullOrWhiteSpace(connectionConfig.Schema))
                {
                    Console.WriteLine($"   Schema: {connectionConfig.Schema}");
                }
                
                Console.WriteLine("   Connecting...");
                
                var connectionResult = dbProvider.GetConnection();
                
                if (connectionResult.Item2) // Success
                {
                    // Test a simple query to make sure the connection really works
                    using (var conn = connectionResult.Item1)
                    {
                        try
                        {
                            using (var cmd = new OracleCommand("SELECT 1 FROM DUAL", conn))
                            {
                                var result = cmd.ExecuteScalar();
                                Console.WriteLine("SUCCESS: Database connection test passed!");
                                Console.WriteLine("   Connected successfully and executed test query.");
                                
                                // Show some additional connection info
                                Console.WriteLine($"   Oracle Version: {conn.ServerVersion}");
                                Console.WriteLine($"   Database Name: {conn.DatabaseName}");
                            }
                        }
                        catch (Exception queryEx)
                        {
                            Console.WriteLine("WARNING: Connected to database but test query failed:");
                            Console.WriteLine($"   {queryEx.Message}");
                        }
                        finally
                        {
                            conn.Close();
                        }
                    }
                }
                else
                {
                    Console.WriteLine("FAILED: Unable to connect to database");
                    Console.WriteLine($"   Error: {connectionResult.Item3}");
                    
                    // Provide some troubleshooting hints
                    Console.WriteLine("\nTROUBLESHOOTING TIPS:");
                    Console.WriteLine("   • Check that TNS_ADMIN path exists and contains tnsnames.ora");
                    Console.WriteLine("   • Verify TNS connection name exists in tnsnames.ora");
                    Console.WriteLine("   • Confirm username and password are correct");
                    Console.WriteLine("   • Ensure database is running and accessible");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION: An unexpected error occurred during connection test:");
                Console.WriteLine($"   {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"   Inner Exception: {ex.InnerException.Message}");
                }
            }
            
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }
    }
}