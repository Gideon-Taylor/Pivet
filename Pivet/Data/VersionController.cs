using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using LibGit2Sharp;
namespace Pivet.Data
{
    internal class VersionController
    {
        private Repository _repository;
        RepositoryConfig config;
        private string _repoBase;
        double lastProgress;
        internal bool InitRepository(string path, RepositoryConfig config)
        {
            this.config = config;
            Logger.Write("Initializing Repository.");
            _repoBase = path;
            if (Directory.Exists(path) == false)
            {
                Directory.CreateDirectory(path);
            }

            if (Directory.Exists(path + Path.DirectorySeparatorChar + ".git"))
            {
                Logger.Write("Repository found, opening.");
                /* already a git repo */
                _repository = new Repository(_repoBase);

            }
            else
            {
                if (config.UsesBranch)
                {
                    /* the branch is fetched on its own below, so the other jobs' branches aren't downloaded */
                    Repository.Init(_repoBase);
                }
                else if (config.Url.Length > 0)
                {
                    Logger.Write("Repository URL is set, cloning repo to disk.");
                    /* repository is configured with a remote, try cloning from there */
                    try
                    {
                        var co = new CloneOptions();
                        
                        co.FetchOptions.CredentialsProvider = (_url, _user, _cred) => new UsernamePasswordCredentials { Username = config.User, Password = config.Password };
                        var result = Repository.Clone(config.Url, _repoBase, co);
                    } catch (Exception ex)
                    {
                        /* Cloning failed, maybe it doesn't exist at the remote yet. Gitlab lets you push to a non-existant repo to create it, so we should continue */
                        Logger.Write("Failed to clone repo, creating new repository.");
                        Repository.Init(_repoBase);
                    }
                }
                else
                {
                    Repository.Init(_repoBase);
                }
                _repository = new Repository(_repoBase);
            }

            /* confirm this repository has an "origin" remote */
            if (_repository.Network.Remotes["origin"] != null)
            {
                /* there is an origin, make sure it points to the URL in config */
                if (_repository.Network.Remotes["origin"].Url.Equals(config.Url) == false)
                {
                    Logger.Write("Repository origin doesn't match config. Updating...");
                    _repository.Network.Remotes.Update("origin", r => r.Url = config.Url);
                }
            }
            else
            {
                Logger.Write("Repository doesn't have an origin. Adding...");
                /* no origin, lets set one if we have a URL */
                if (config.Url.Length > 0)
                {
                    _repository.Network.Remotes.Add("origin", config.Url);
                }
            }

            if (config.UsesBranch && SwitchToBranch(config.Branch.Trim()) == false)
            {
                return false;
            }

            /* confirm .gitignore is present */
            var gitAttrFile = Path.Join(_repoBase, ".gitattributes");
            if (File.Exists(gitAttrFile) == false)
            {
                Logger.Write("Repository does not have a gitattributes present. Adding...");
                File.WriteAllText(gitAttrFile, "* text eol=crlf");
                Commands.Stage(_repository, gitAttrFile);

                Signature author = new Signature("PIVET", "PIVET", DateTime.Now);
                Signature committer = author;
                Commit commit = _repository.Commit("Added gitattributes for consistent line endings", author, committer);
            }

            return true;
        }

        /* Puts the working folder on the job's branch: the local branch if there is one, otherwise the
         * branch from origin, otherwise a new branch with its own history (like git switch --orphan). */
        private bool SwitchToBranch(string branch)
        {
            string branchRef = "refs/heads/" + branch;
            if (Reference.IsValidName(branchRef) == false)
            {
                Logger.Error($"'{branch}' is not a valid branch name.");
                return false;
            }

            if (_repository.Head.CanonicalName == branchRef)
            {
                return true;
            }

            Branch local = _repository.Branches[branch];
            if (local == null && config.Url.Length > 0)
            {
                IEnumerable<Reference> remoteRefs;
                try
                {
                    remoteRefs = Repository.ListRemoteReferences(config.Url, Credentials).ToList();
                }
                catch (Exception ex)
                {
                    /* don't start a new branch on a failed lookup: a branch that does exist would then be rejected on push */
                    Logger.Error("Unable to read the branches on origin, so the job was stopped. Check the URL and credentials, and create the repository first if it doesn't exist. " + ex.Message);
                    return false;
                }

                if (remoteRefs.Any(r => r.CanonicalName == branchRef))
                {
                    Logger.Write($"Fetching branch '{branch}' from origin.");
                    string trackingRef = "refs/remotes/origin/" + branch;
                    Commands.Fetch(_repository, "origin", new[] { $"+{branchRef}:{trackingRef}" }, new FetchOptions() { CredentialsProvider = Credentials }, null);
                    Branch tracking = _repository.Branches[trackingRef];
                    local = _repository.CreateBranch(branch, tracking.Tip);
                    _repository.Branches.Update(local, b => b.TrackedBranch = tracking.CanonicalName);
                }
            }

            if (local != null)
            {
                Logger.Write($"Switching to branch '{branch}'.");
                /* Force: every file is written again from the database, so local changes don't need keeping */
                Commands.Checkout(_repository, local, new CheckoutOptions() { CheckoutModifiers = CheckoutModifiers.Force });
            }
            else
            {
                Logger.Write($"Branch '{branch}' doesn't exist yet, starting it with its own history.");
                foreach (var entry in _repository.Index.ToList())
                {
                    var file = Path.Combine(_repoBase, entry.Path);
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                _repository.Index.Clear();
                _repository.Index.Write();
                _repository.Refs.UpdateTarget("HEAD", branchRef);
            }
            return true;
        }

        private Credentials Credentials(string url, string usernameFromUrl, SupportedCredentialTypes types)
        {
            return new UsernamePasswordCredentials { Username = config.User, Password = config.Password };
        }

        private void ReportProgress(double progress)
        {
            if (Program.ShowProgress)
            {
                if (lastProgress != progress)
                {
                    Console.CursorLeft = 0;
                    if (Console.CursorTop > 0)
                    {
                        Console.CursorTop--;
                    }

                    Logger.Write("Progress: " + string.Format("{0:N2}%", progress));
                    lastProgress = progress;
                }
            }
        }

        internal bool ProcessChanges(List<ChangedItem> adds)
        {
            Logger.Write("Processing repository changes...");
            Logger.Write("");
            double current = 0;
            ReportProgress(0);

            List<string> changedOrNewItems = _repository.RetrieveStatus().Where(p => p.State == FileStatus.ModifiedInWorkdir || p.State == FileStatus.NewInWorkdir).Select(o => o.FilePath).ToList();
            List<string> deletedFiles = _repository.RetrieveStatus().Where(p => p.State == FileStatus.DeletedFromWorkdir || p.State == FileStatus.DeletedFromIndex).Select(o => o.FilePath).ToList();
            double total = 0;
            if (config.CommitByOprid)
            {
                List<ChangedItem> newOrModifiedFiles = new List<ChangedItem>();
                total = changedOrNewItems.Count;

                foreach (var f in changedOrNewItems)
                {
                    newOrModifiedFiles.Add(adds.Where(p => p.RepoPath == f).First());
                    current++;
                    ReportProgress(((int)(((current / total) * 10000)) / (double)100));
                }

                Logger.Write("Processing OPRID groups...");
                var opridGroups = newOrModifiedFiles.GroupBy(p => p.OperatorId);

                Logger.Write("Processing staged changes...");
                Logger.Write("");
                foreach (var opr in opridGroups)
                {
                    var staged = opr.Select(o => o.RepoPath).ToList();
                    Commands.Stage(_repository, staged);

                    if (newOrModifiedFiles.Count > 0)
                    {
                        var oprid = opr.Key;
                        Signature author = new Signature(oprid, oprid, DateTime.Now);
                        Signature committer = author;
                        Commit commit = _repository.Commit("Changes made by " + oprid, author, committer, new CommitOptions() { AllowEmptyCommit = true });
                    }
                }
            }
            else //not by oprid
            {
                if (changedOrNewItems.Count > 0)
                {
                    if (config.CommitStyle == CommitStyleOptions.SINGLE_COMMIT)
                    {
                        Commands.Stage(_repository, changedOrNewItems);
                        Signature author = new Signature("PIVET", "PIVET", DateTime.Now);
                        Signature committer = author;
                        String commitString = "Changes captured by Pivet";
                        if (Program.CustomCommitMessage.Length > 0)
                        {
                            commitString = Program.CustomCommitMessage;
                        }
                        Commit commit = _repository.Commit(commitString, author, committer, new CommitOptions() { AllowEmptyCommit = true });
                    }
                    else if (config.CommitStyle == CommitStyleOptions.PEOPLECODE_SEPARATE)
                    {
                        Logger.Write("Starting PeopleCode separate processing.");
                        List<String> peoplecodeItems = changedOrNewItems.Where(s => s.StartsWith("PeopleCode/")).ToList();
                        List<String> everythingElse = changedOrNewItems.Where(s => s.StartsWith($"PeopleCode/") == false).ToList();
                        Logger.Write("Peoplecode items: " + peoplecodeItems.Count);
                        Logger.Write("Everything else items: " + everythingElse.Count);
                        Signature author, committer;
                        String commitString;
                        Commit commit;
                        /* Commit PeopleCode changes */
                        if (peoplecodeItems.Count > 0) {
                            Commands.Stage(_repository, peoplecodeItems);
                            author = new Signature("PIVET", "PIVET", DateTime.Now);
                            committer = author;
                            commitString = "PeopleCode changes captured by Pivet";
                            if (Program.CustomCommitMessage.Length > 0)
                            {
                                commitString = "[PeopleCode] " + Program.CustomCommitMessage;
                            }
                            commit = _repository.Commit(commitString, author, committer, new CommitOptions() { AllowEmptyCommit = true });
                        }
                        /* Commit everything else */
                        if (everythingElse.Count > 0) {
                            Commands.Stage(_repository, everythingElse);
                            author = new Signature("PIVET", "PIVET", DateTime.Now);
                            committer = author;
                            commitString = "Non-PeopleCode changes captured by Pivet";
                            if (Program.CustomCommitMessage.Length > 0)
                            {
                                commitString = "[Non-PeopleCode] " + Program.CustomCommitMessage;
                            }
                            commit = _repository.Commit(commitString, author, committer, new CommitOptions() { AllowEmptyCommit = true });
                        }
                    }
                    else if (config.CommitStyle == CommitStyleOptions.TOP_LEVEL_SEPARATE)
                    {
                        Logger.Write("Processing Top Level Separate Commit Style");
                        Signature author;
                        Signature committer;
                        string commitString;
                        Commit commit;
                        var topLevelFolders = Directory.EnumerateDirectories(_repoBase).Select(d => d.Split(Path.DirectorySeparatorChar).Last()).ToList();
                        foreach (var topLevelFolder in topLevelFolders)
                        {
                            /* do not process the .git folder */
                            if (topLevelFolder.Equals(".git")) {
                                continue;
                            }
                            Logger.Write($"Processing Top Level: {topLevelFolder}");
                            var topItems = changedOrNewItems.Where(s => s.StartsWith($"{topLevelFolder}/")).ToList();
                            Logger.Write($"  Item Count: {topItems.Count}");
                            if (topItems.Count > 0) {
                                Commands.Stage(_repository, topItems);
                                author = new Signature("PIVET", "PIVET", DateTime.Now);
                                committer = author;
                                commitString = $"{topLevelFolder} changes captured by Pivet";
                                if (Program.CustomCommitMessage.Length > 0)
                                {
                                    commitString = $"[{topLevelFolder}] " + Program.CustomCommitMessage;
                                }
                                commit = _repository.Commit(commitString, author, committer, new CommitOptions() { AllowEmptyCommit = true });
                            }
                        }

                        /* Get updated repo status to capture any top level items that need to be commited */
                        changedOrNewItems = _repository.RetrieveStatus().Where(p => p.State == FileStatus.ModifiedInWorkdir || p.State == FileStatus.NewInWorkdir).Select(o => o.FilePath).ToList();
                        if (changedOrNewItems.Count > 0) {
                            Commands.Stage(_repository, changedOrNewItems);
                            author = new Signature("PIVET", "PIVET", DateTime.Now);
                            committer = author;
                            commitString = "[Top level] Changes captured by Pivet";
                            if (Program.CustomCommitMessage.Length > 0)
                            {
                                commitString = Program.CustomCommitMessage;
                            }
                            commit = _repository.Commit(commitString, author, committer, new CommitOptions() { AllowEmptyCommit = true });
                        }
                    }
                }
            }

            if (deletedFiles.Count > 0)
            {
                Commands.Stage(_repository, deletedFiles);
                Signature author = new Signature("PIVET", "PIVET", DateTime.Now);
                Signature committer = author;
                Commit commit = _repository.Commit("Deleted Objects", author, committer, new CommitOptions() { AllowEmptyCommit = true });
            }

            /* We have commited all changes, time to push if setup with a URL */
            if (this.config.Url.Length > 0)
            {
                Logger.Write("Pushing repository to origin.");
                Remote remote = _repository.Network.Remotes["origin"];
                var options = new PushOptions();
                options.CredentialsProvider = (_url, _user, _cred) =>
                    new UsernamePasswordCredentials { Username = this.config.User, Password = this.config.Password };
                options.OnPackBuilderProgress = (x, y, z) =>
                {
                    Console.CursorLeft = 0;
                    if (Console.CursorTop > 0)
                    {
                        Console.CursorTop--;
                    }
                    Logger.Write(x + " (" + y + "/" + z + ")                              ");
                    return true;
                };

                options.OnPushTransferProgress = (x, y, z) =>
                {
                    Console.CursorLeft = 0;
                    if (Console.CursorTop > 0)
                    {
                        Console.CursorTop--;
                    }
                    Logger.Write("Pushing objects (" + x + "/" + y + ")                                 ");
                    return true;
                };

                options.OnPushStatusError = (x) =>
                {
                    Logger.Write("Push Error: " + x.Message);
                };
                try
                {
                    _repository.Network.Push(remote, config.PushRef, options);
                }
                catch (Exception ex)
                {
                    Logger.Write("Exception occured while pushing to remote: " + ex.ToString());
                    return false;
                }
                return true;
            }
            return true;
        }
    }
}
