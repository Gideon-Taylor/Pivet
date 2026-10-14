using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Pivet.GUI
{
    /// <summary>
    /// Validation framework for Pivet configuration objects
    /// </summary>
    public static class ConfigValidator
    {
        /// <summary>
        /// Validates an entire configuration and returns all validation results
        /// </summary>
        public static ConfigValidationResult ValidateConfig(Config config)
        {
            var result = new ConfigValidationResult();
            
            if (config == null)
            {
                result.AddError("Configuration is null", "Config");
                return result;
            }

            // Validate environments
            ValidateEnvironments(config.Environments, result);
            
            // Validate profiles
            ValidateProfiles(config.Profiles, result);
            
            // Validate jobs
            ValidateJobs(config.Jobs, config, result);

            return result;
        }

        /// <summary>
        /// Validates a specific environment configuration
        /// </summary>
        public static ConfigValidationResult ValidateEnvironment(EnvironmentConfig environment)
        {
            var result = new ConfigValidationResult();
            
            if (environment == null)
            {
                result.AddError("Environment configuration is null", "Environment");
                return result;
            }

            ValidateEnvironmentConfig(environment, result, "Environment");
            return result;
        }

        /// <summary>
        /// Validates a specific profile configuration  
        /// </summary>
        public static ConfigValidationResult ValidateProfile(ProfileConfig profile)
        {
            var result = new ConfigValidationResult();
            
            if (profile == null)
            {
                result.AddError("Profile configuration is null", "Profile");
                return result;
            }

            ValidateProfileConfig(profile, result, "Profile");
            return result;
        }

        /// <summary>
        /// Validates a specific job configuration
        /// </summary>
        public static ConfigValidationResult ValidateJob(JobConfig job, Config config)
        {
            var result = new ConfigValidationResult();
            
            if (job == null)
            {
                result.AddError("Job configuration is null", "Job");
                return result;
            }

            ValidateJobConfig(job, config, result, "Job");
            return result;
        }

        private static void ValidateEnvironments(List<EnvironmentConfig> environments, ConfigValidationResult result)
        {
            if (environments == null || environments.Count == 0)
            {
                result.AddWarning("No environments defined", "Environments");
                return;
            }

            var names = new HashSet<string>();
            for (int i = 0; i < environments.Count; i++)
            {
                var env = environments[i];
                var context = $"Environment[{i}]";
                
                ValidateEnvironmentConfig(env, result, context);
                
                // Check for duplicate names
                if (!string.IsNullOrWhiteSpace(env.Name))
                {
                    if (names.Contains(env.Name))
                    {
                        result.AddError($"Duplicate environment name: {env.Name}", context);
                    }
                    names.Add(env.Name);
                }
            }
        }

        private static void ValidateEnvironmentConfig(EnvironmentConfig env, ConfigValidationResult result, string context)
        {
            if (env == null) return;

            // Validate required fields
            if (string.IsNullOrWhiteSpace(env.Name))
            {
                result.AddError("Environment name is required", $"{context}.Name");
            }

            // Validate connection
            if (env.Connection == null)
            {
                result.AddError("Connection configuration is required", $"{context}.Connection");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(env.Connection.TNS))
                {
                    result.AddError("TNS name is required", $"{context}.Connection.TNS");
                }

                if (string.IsNullOrWhiteSpace(env.Connection.TNS_ADMIN))
                {
                    result.AddWarning("TNS_ADMIN path not specified", $"{context}.Connection.TNS_ADMIN");
                }
                else if (!Directory.Exists(env.Connection.TNS_ADMIN))
                {
                    result.AddWarning($"TNS_ADMIN directory does not exist: {env.Connection.TNS_ADMIN}", $"{context}.Connection.TNS_ADMIN");
                }

                // Validate bootstrap parameters
                if (env.Connection.BootstrapParameters == null)
                {
                    result.AddWarning("Bootstrap parameters not specified", $"{context}.Connection.BootstrapParameters");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(env.Connection.BootstrapParameters.User))
                    {
                        result.AddError("Bootstrap user is required", $"{context}.Connection.BootstrapParameters.User");
                    }

                    if (string.IsNullOrWhiteSpace(env.Connection.BootstrapParameters.EncryptedPassword))
                    {
                        result.AddError("Bootstrap password is required", $"{context}.Connection.BootstrapParameters.Password");
                    }
                }
            }
        }

        private static void ValidateProfiles(List<ProfileConfig> profiles, ConfigValidationResult result)
        {
            if (profiles == null || profiles.Count == 0)
            {
                result.AddWarning("No profiles defined", "Profiles");
                return;
            }

            var names = new HashSet<string>();
            for (int i = 0; i < profiles.Count; i++)
            {
                var profile = profiles[i];
                var context = $"Profile[{i}]";
                
                ValidateProfileConfig(profile, result, context);
                
                // Check for duplicate names
                if (!string.IsNullOrWhiteSpace(profile.Name))
                {
                    if (names.Contains(profile.Name))
                    {
                        result.AddError($"Duplicate profile name: {profile.Name}", context);
                    }
                    names.Add(profile.Name);
                }
            }
        }

        private static void ValidateProfileConfig(ProfileConfig profile, ConfigValidationResult result, string context)
        {
            if (profile == null) return;

            // Validate required fields
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                result.AddError("Profile name is required", $"{context}.Name");
            }

            // Validate data providers
            if (profile.DataProviders == null || profile.DataProviders.Count == 0)
            {
                result.AddWarning("No data providers specified", $"{context}.DataProviders");
            }
            else
            {
                var availableProcessors = DataProcessorService.GetAvailableProcessors();
                for (int i = 0; i < profile.DataProviders.Count; i++)
                {
                    var provider = profile.DataProviders[i];
                    if (string.IsNullOrWhiteSpace(provider))
                    {
                        result.AddError("Data provider name cannot be empty", $"{context}.DataProviders[{i}]");
                    }
                    else if (!availableProcessors.Contains(provider))
                    {
                        result.AddWarning($"Data provider '{provider}' is not a recognized processor", $"{context}.DataProviders[{i}]");
                    }
                }
            }

            // Validate filters (basic validation for now)
            if (profile.Filters == null)
            {
                result.AddInfo("No filters specified - all data will be processed", $"{context}.Filters");
            }
        }

        private static void ValidateJobs(List<JobConfig> jobs, Config config, ConfigValidationResult result)
        {
            if (jobs == null || jobs.Count == 0)
            {
                result.AddWarning("No jobs defined", "Jobs");
                return;
            }

            var names = new HashSet<string>();
            for (int i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                var context = $"Job[{i}]";
                
                ValidateJobConfig(job, config, result, context);
                
                // Check for duplicate names
                if (!string.IsNullOrWhiteSpace(job.Name))
                {
                    if (names.Contains(job.Name))
                    {
                        result.AddError($"Duplicate job name: {job.Name}", context);
                    }
                    names.Add(job.Name);
                }
            }
        }

        private static void ValidateJobConfig(JobConfig job, Config config, ConfigValidationResult result, string context)
        {
            if (job == null) return;

            // Validate required fields
            if (string.IsNullOrWhiteSpace(job.Name))
            {
                result.AddError("Job name is required", $"{context}.Name");
            }

            if (string.IsNullOrWhiteSpace(job.OutputFolder))
            {
                result.AddError("Output folder is required", $"{context}.OutputFolder");
            }
            else
            {
                try
                {
                    var parentDir = Path.GetDirectoryName(job.OutputFolder);
                    if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                    {
                        result.AddWarning($"Output folder parent directory does not exist: {parentDir}", $"{context}.OutputFolder");
                    }
                }
                catch (Exception ex)
                {
                    result.AddError($"Invalid output folder path: {ex.Message}", $"{context}.OutputFolder");
                }
            }

            // Validate environment reference
            if (string.IsNullOrWhiteSpace(job.EnvironmentName))
            {
                result.AddError("Environment name is required", $"{context}.EnvironmentName");
            }
            else if (config?.Environments != null)
            {
                if (!config.Environments.Any(e => e.Name == job.EnvironmentName))
                {
                    result.AddError($"Referenced environment '{job.EnvironmentName}' does not exist", $"{context}.EnvironmentName");
                }
            }

            // Validate profile reference
            if (string.IsNullOrWhiteSpace(job.ProfileName))
            {
                result.AddError("Profile name is required", $"{context}.ProfileName");
            }
            else if (config?.Profiles != null)
            {
                if (!config.Profiles.Any(p => p.Name == job.ProfileName))
                {
                    result.AddError($"Referenced profile '{job.ProfileName}' does not exist", $"{context}.ProfileName");
                }
            }

            // Validate repository configuration
            if (job.Repository == null)
            {
                result.AddWarning("Repository configuration not specified", $"{context}.Repository");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(job.Repository.Url))
                {
                    result.AddWarning("Repository URL not specified", $"{context}.Repository.Url");
                }

                if (string.IsNullOrWhiteSpace(job.Repository.User))
                {
                    result.AddWarning("Repository user not specified", $"{context}.Repository.User");
                }

                if (string.IsNullOrWhiteSpace(job.Repository.EncryptedPassword))
                {
                    result.AddWarning("Repository password not specified", $"{context}.Repository.Password");
                }

                if (job.Repository.UsesBranch && LibGit2Sharp.Reference.IsValidName(job.Repository.PushRef) == false)
                {
                    result.AddError($"'{job.Repository.Branch}' is not a valid branch name", $"{context}.Repository.Branch");
                }

                if (job.Repository.MaxFileLossPercent is int loss && (loss < 0 || loss > 100))
                {
                    result.AddError($"MaxFileLossPercent must be between 0 and 100 (it is {loss})", $"{context}.Repository.MaxFileLossPercent");
                }

                if (job.Repository.UsesBranch && config?.Jobs != null)
                {
                    var sharing = config.Jobs.Where(j => j != job && j.Repository != null
                        && string.Equals(j.Repository.Url, job.Repository.Url, StringComparison.OrdinalIgnoreCase)
                        && j.Repository.PushRef == job.Repository.PushRef).Select(j => j.Name).ToList();
                    if (sharing.Count > 0)
                    {
                        result.AddWarning($"Branch '{job.Repository.Branch}' is also used by job(s) {string.Join(", ", sharing)} on the same repository", $"{context}.Repository.Branch");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Result of configuration validation
    /// </summary>
    public class ConfigValidationResult
    {
        public List<ValidationMessage> Errors { get; } = new List<ValidationMessage>();
        public List<ValidationMessage> Warnings { get; } = new List<ValidationMessage>();
        public List<ValidationMessage> InfoMessages { get; } = new List<ValidationMessage>();

        public bool IsValid => Errors.Count == 0;
        public bool HasWarnings => Warnings.Count > 0;
        public bool HasMessages => Errors.Count > 0 || Warnings.Count > 0 || InfoMessages.Count > 0;

        public void AddError(string message, string field = null)
        {
            Errors.Add(new ValidationMessage(ValidationLevel.Error, message, field));
        }

        public void AddWarning(string message, string field = null)
        {
            Warnings.Add(new ValidationMessage(ValidationLevel.Warning, message, field));
        }

        public void AddInfo(string message, string field = null)
        {
            InfoMessages.Add(new ValidationMessage(ValidationLevel.Info, message, field));
        }

        public List<ValidationMessage> GetAllMessages()
        {
            var all = new List<ValidationMessage>();
            all.AddRange(Errors);
            all.AddRange(Warnings);
            all.AddRange(InfoMessages);
            return all.OrderBy(m => m.Field).ToList();
        }
    }

    /// <summary>
    /// A single validation message
    /// </summary>
    public class ValidationMessage
    {
        public ValidationLevel Level { get; }
        public string Message { get; }
        public string Field { get; }
        public DateTime Timestamp { get; }

        public ValidationMessage(ValidationLevel level, string message, string field = null)
        {
            Level = level;
            Message = message;
            Field = field;
            Timestamp = DateTime.Now;
        }

        public override string ToString()
        {
            var prefix = Level switch
            {
                ValidationLevel.Error => "[ERROR]",
                ValidationLevel.Warning => "[WARN]",
                ValidationLevel.Info => "[INFO]",
                _ => "[UNKNOWN]"
            };

            var fieldPart = string.IsNullOrWhiteSpace(Field) ? "" : $" ({Field})";
            return $"{prefix} {Message}{fieldPart}";
        }
    }

    /// <summary>
    /// Validation message severity levels
    /// </summary>
    public enum ValidationLevel
    {
        Info,
        Warning,
        Error
    }
}