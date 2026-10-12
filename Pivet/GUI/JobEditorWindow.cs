using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terminal.Gui;

namespace Pivet.GUI
{
    internal class JobEditorWindow : Window
    {
        private ListView jobList;
        private Config current_config;
        private JobConfig selectedJob;

        private bool dirtyFlag = false;
        private TextField jobName;
        private TextField outputFolder;
        private ComboBox envChoice;
        private ComboBox profileChoice;
        private Button saveButton;
        private TextField userID;
        private TextField password;
        private TextField remoteURL;
        private TextField branch;
        private ComboBox commitStyle;
        private CheckBox commitByOprid;

        internal void MarkDirty(NStack.ustring newValue)
        {
            saveButton.Visible = true;
            saveButton.SetNeedsDisplay();
            dirtyFlag = true;
        }

        internal void SaveJobData()
        {
            if (selectedJob == null) return;
            
            selectedJob.Name = jobName.Text.ToString();
            selectedJob.OutputFolder = outputFolder.Text.ToString();
            
            // Set environment name from selected combo box
            if (envChoice.SelectedItem >= 0 && envChoice.SelectedItem < current_config.Environments.Count)
            {
                selectedJob.EnvironmentName = current_config.Environments[envChoice.SelectedItem].Name;
            }
            
            // Set profile name from selected combo box
            if (profileChoice.SelectedItem >= 0 && profileChoice.SelectedItem < current_config.Profiles.Count)
            {
                selectedJob.ProfileName = current_config.Profiles[profileChoice.SelectedItem].Name;
            }
            
            // Save repository configuration
            selectedJob.Repository.User = userID.Text.ToString();
            selectedJob.Repository.EncryptedPassword = password.Text.ToString();
            selectedJob.Repository.Url = remoteURL.Text.ToString();
            var branchName = branch.Text.ToString().Trim();
            selectedJob.Repository.Branch = branchName.Length > 0 ? branchName : null;
            selectedJob.Repository.CommitByOprid = commitByOprid.Checked;
            
            // Set commit style from combo box
            if (commitStyle.SelectedItem >= 0)
            {
                switch (commitStyle.SelectedItem)
                {
                    case 0:
                        selectedJob.Repository.CommitStyle = CommitStyleOptions.SINGLE_COMMIT;
                        break;
                    case 1:
                        selectedJob.Repository.CommitStyle = CommitStyleOptions.PEOPLECODE_SEPARATE;
                        break;
                    case 2:
                        selectedJob.Repository.CommitStyle = CommitStyleOptions.TOP_LEVEL_SEPARATE;
                        break;
                }
            }
            
            // Validate the job before saving
            var validationResult = ConfigValidator.ValidateJob(selectedJob, current_config);
            if (!validationResult.IsValid)
            {
                var errorMessages = string.Join("\n", validationResult.Errors.Select(e => e.Message));
                MessageBox.ErrorQuery("Validation Errors", $"Cannot save job:\n{errorMessages}", "OK");
                return;
            }
            
            if (validationResult.HasWarnings)
            {
                var warningMessages = string.Join("\n", validationResult.Warnings.Select(w => w.Message));
                var result = MessageBox.Query("Validation Warnings", $"Job has warnings:\n{warningMessages}\n\nSave anyway?", "Yes", "No");
                if (result != 0) return;
            }
            
            saveButton.Visible = false;
            saveButton.SetNeedsDisplay();
            dirtyFlag = false;
        }

        internal void PromptForSaveIfNeeded()
        {
            if (dirtyFlag)
            {
                var result = MessageBox.Query("Save Changes?", "Would you like to save changes to the current job?", "Yes", "No");
                if (result == 0)
                {
                    SaveJobData();
                }
            }
        }

        internal void SelectJob(int index)
        {
            PromptForSaveIfNeeded();

            bool createdDefaultJob = false;

            if (current_config.Jobs.Count == 0)
            {
                /* need to make an initial Environment */
                current_config.Jobs.Add(new JobConfig() { Name = "<unnamed>" });
                createdDefaultJob = true;
            }

            selectedJob = current_config.Jobs[index];

            if (jobName == null)
            {
                /* UI hasn't been built yet */
                return;
            }

            // Populate job fields
            jobName.Text = selectedJob.Name;
            outputFolder.Text = selectedJob.OutputFolder;
            
            // Set environment combo box selection
            for (int i = 0; i < current_config.Environments.Count; i++)
            {
                if (current_config.Environments[i].Name == selectedJob.EnvironmentName)
                {
                    envChoice.SelectedItem = i;
                    break;
                }
            }
            
            // Set profile combo box selection
            for (int i = 0; i < current_config.Profiles.Count; i++)
            {
                if (current_config.Profiles[i].Name == selectedJob.ProfileName)
                {
                    profileChoice.SelectedItem = i;
                    break;
                }
            }
            
            // Populate repository fields
            userID.Text = selectedJob.Repository.User;
            password.Text = selectedJob.Repository.EncryptedPassword;
            remoteURL.Text = selectedJob.Repository.Url;
            branch.Text = selectedJob.Repository.Branch ?? "";
            commitByOprid.Checked = selectedJob.Repository.CommitByOprid;
            
            // Set commit style combo box
            switch (selectedJob.Repository.CommitStyle)
            {
                case CommitStyleOptions.SINGLE_COMMIT:
                    commitStyle.SelectedItem = 0;
                    break;
                case CommitStyleOptions.PEOPLECODE_SEPARATE:
                    commitStyle.SelectedItem = 1;
                    break;
                case CommitStyleOptions.TOP_LEVEL_SEPARATE:
                    commitStyle.SelectedItem = 2;
                    break;
            }

            dirtyFlag = createdDefaultJob;
        }

        internal JobEditorWindow(Config config) : base("Jobs")
        {
            current_config = config;
            var listFrame = new FrameView()
            {
                X = 1,
                Y = 1,
                Height = Dim.Fill(2),
                Width = Dim.Percent(25)
            };

            jobList = new ListView()
            {
                X = 0,
                Y = 0,
                Height = Dim.Fill(),
                Width = Dim.Fill()
            };
            var wrapper = new ListWrapper(current_config.Jobs);
            jobList.Source = wrapper;

            jobList.SelectedItemChanged += JobList_SelectedItemChanged;

            listFrame.Add(jobList);
            Add(listFrame);

            var createJobButton = new Button("Create Job")
            {
                X = 1,
                Y = Pos.Bottom(listFrame),
                Height = 1,
                Width = Dim.Percent(25),
                TextAlignment = TextAlignment.Centered
            };

            createJobButton.Clicked += CreateJobButton_Clicked;

            Add(createJobButton);

            var escapeMessage = new Label("Press [ESC] to close.")
            {
                X = Pos.Right(createJobButton) - 2,
                Y = Pos.Top(createJobButton),
                Height = 1,
                Width = Dim.Percent(75),
                TextAlignment = TextAlignment.Right
            };
            Add(escapeMessage);

            var _scrollBar = new ScrollBarView(jobList, true);

            _scrollBar.ChangedPosition += () =>
            {
                jobList.TopItem = _scrollBar.Position;
                if (jobList.TopItem != _scrollBar.Position)
                {
                    _scrollBar.Position = jobList.TopItem;
                }
                jobList.SetNeedsDisplay();
            };

            _scrollBar.OtherScrollBarView.ChangedPosition += () =>
            {
                jobList.LeftItem = _scrollBar.OtherScrollBarView.Position;
                if (jobList.LeftItem != _scrollBar.OtherScrollBarView.Position)
                {
                    _scrollBar.OtherScrollBarView.Position = jobList.LeftItem;
                }
                jobList.SetNeedsDisplay();
            };

            jobList.DrawContent += (e) =>
            {
                _scrollBar.Size = jobList.Source.Count - 1;
                _scrollBar.Position = jobList.TopItem;
                _scrollBar.OtherScrollBarView.Size = jobList.Maxlength - 1;
                _scrollBar.OtherScrollBarView.Position = jobList.LeftItem;
                _scrollBar.Refresh();
            };

            /* Setup Right pane which has the details for an environment */
            /* Name */
            /* Connection: */
            /*   Connection Provider
             *   TNS
             *   TNS_ADMIN
             *   Schema
             *   Bootstrap Params:
             *      User
             *      Password */

            var jobDetailsFrame = new FrameView("Details")
            {
                X = Pos.Right(listFrame),
                Y = 1,
                Width = Dim.Fill(2),
                Height = Dim.Fill(2)
            };

            Add(jobDetailsFrame);

            var label = new Label("Job Name: ")
            {
                X = 1,
                Y = 1,
                Height = 1,
                Width = 16
            };


            jobName = new TextField()
            {
                X = Pos.Right(label),
                Y = 1,
                Width = 40
            };
            jobDetailsFrame.Add(label);
            jobDetailsFrame.Add(jobName);

            label = new Label("Output Folder: ")
            {
                X = 1,
                Y = Pos.Bottom(jobName) + 1,
                Height = 1,
                Width = 16
            };

            outputFolder = new TextField()
            {
                Height = 1,
                Width = 40,
                X = Pos.Right(label),
                Y = Pos.Top(label)
            };

            var button = new Button("...")
            {
                X = Pos.Right(outputFolder) + 1,
                Y = Pos.Top(outputFolder),
                Height = 1,
                Width = 7,

            };

            jobDetailsFrame.Add(label, outputFolder, button);
            button.Clicked += outputFolderButton_Clicked; ;

            label = new Label("Environment: ")
            {
                X = 1,
                Y = Pos.Bottom(outputFolder) + 1,
                Height = 1,
                Width = 16
            };

            envChoice = new ComboBox("")
            {
                Height = 7,
                Width = 20,
                X = Pos.Right(label),
                Y = Pos.Top(label),
                ColorScheme = Colors.Dialog
            };
            
            envChoice.SetSource(current_config.Environments);

            jobDetailsFrame.Add(label, envChoice);

            label = new Label("Profile: ")
            {
                X = 1,
                Y = Pos.Top(envChoice) + 2,
                Height = 1,
                Width = 16
            };

            profileChoice = new ComboBox()
            {
                Height = 7,
                Width = 20,
                X = Pos.Right(label),
                Y = Pos.Top(label),
                ColorScheme = Colors.Dialog
            };
            profileChoice.SetSource(current_config.Profiles);

            jobDetailsFrame.Add(label, profileChoice);

            /* repository details */
            var repoFrame = new FrameView("Remote Repository")
            {
                Height = Dim.Fill() - 3,
                Width = Dim.Fill(),
                X = 1,
                Y = Pos.Top(profileChoice) + 2
            };

            jobDetailsFrame.Add(repoFrame);

            label = new Label("Commit Style: ")
            {
                X = 1,
                Y = 1,
                AutoSize = true
            };

            commitStyle = new ComboBox("")
            {
                X = Pos.Right(label),
                Y = Pos.Top(label),
                Height = 4,
                Width = 25,
                ColorScheme = Colors.Dialog
            };
            commitStyle.SetSource(new string[] { "Single Commit","PeopleCode Separate","Top Level Folders"});
            repoFrame.Add(label, commitStyle);

            commitByOprid = new CheckBox("Commit By Oprid")
            {
                X = Pos.Right(commitStyle) + 2,
                Y = Pos.Top(commitStyle),
                AutoSize = true
            };
            repoFrame.Add(commitByOprid);

            label = new Label("Git User: ")
            {
                X = 1,
                Y = Pos.Top(commitStyle) + 2,
                AutoSize = true
            };

            userID = new TextField()
            {
                X = Pos.Right(label),
                Y = Pos.Top(label),
                Height = 1,
                Width = 20
            };

            repoFrame.Add(label, userID);

            label = new Label("Git Password: ")
            {
                X = Pos.Right(userID) + 1,
                Y = Pos.Top(userID),
                AutoSize = true
            };

            password = new TextField()
            {
                X = Pos.Right(label),
                Y = Pos.Top(label),
                Height = 1,
                Width = 20,
                Secret = true
            };

            repoFrame.Add(label, password);

            button = new Button("Encrypt")
            {
                X = Pos.Right(password) + 1,
                Y = Pos.Top(password),
                Height = 1,
                Width = 11
            };

            button.Clicked += encryptButtonClick;

            repoFrame.Add(button);


            label = new Label("Remote URL: ")
            {
                X = 1,
                Y = Pos.Bottom(userID) + 1,
                AutoSize = true
            };

            remoteURL = new TextField()
            {
                X = Pos.Right(label),
                Y = Pos.Top(label),
                Height = 1,
                Width = Dim.Fill(1)
            };

            repoFrame.Add(label, remoteURL);

            label = new Label("Branch: ")
            {
                X = 1,
                Y = Pos.Bottom(remoteURL) + 1,
                AutoSize = true
            };

            branch = new TextField()
            {
                X = Pos.Right(label),
                Y = Pos.Top(label),
                Height = 1,
                Width = 30
            };

            var branchHint = new Label("(blank for master)")
            {
                X = Pos.Right(branch) + 1,
                Y = Pos.Top(branch),
                AutoSize = true
            };

            repoFrame.Add(label, branch, branchHint);

            saveButton = new Button("Save Changes")
            {
                X = 1,
                Y = Pos.Bottom(repoFrame) + 1,
                AutoSize = true,
                Visible = false
            };

            saveButton.Clicked += () =>
            {
                SaveJobData();
                jobName.SetFocus();
                saveButton.SetNeedsDisplay();
            };

            jobDetailsFrame.Add(saveButton);


            if (!dirtyFlag)
            {
                SelectJob(0);
            }

            /* keyboard shortcuts */
            KeyUp += (e) => {
                switch (e.KeyEvent.Key)
                {
                    case Key.Esc:
                        RequestStop();
                        break;
                    case Key.CtrlMask | Key.S:
                        if (saveButton.Visible)
                            SaveJobData();
                        break;
                    case Key.CtrlMask | Key.N:
                        CreateJobButton_Clicked();
                        break;
                }
            };

            jobName.TextChanged += MarkDirty;
            outputFolder.TextChanged += MarkDirty;
            envChoice.SelectedItemChanged += MarkDirty_List;
            profileChoice.SelectedItemChanged += MarkDirty_List;
            userID.TextChanged += MarkDirty;
            password.TextChanged += MarkDirty;
            remoteURL.TextChanged += MarkDirty;
            branch.TextChanged += MarkDirty;
            commitStyle.SelectedItemChanged += MarkDirty_List;
            commitByOprid.Toggled += MarkDirty_Checkbox;
            Closing += JobEditorWindow_Closing;
        }

        private void MarkDirty_List(ListViewItemEventArgs obj)
        {
            MarkDirty("");
        }

        private void MarkDirty_Checkbox(bool value)
        {
            MarkDirty("");
        }

        private void outputFolderButton_Clicked()
        {
            var od = new OpenDialog("Output Folder", "Select output folder", null, OpenDialog.OpenMode.Directory);
            Application.Run(od);
            if (od.DirectoryPath.Length > 0)
            {
                outputFolder.Text = od.DirectoryPath.ToString() + Path.DirectorySeparatorChar + od.FilePath.ToString();
            }
        }

        private void JobEditorWindow_Closing(ToplevelClosingEventArgs obj)
        {
            PromptForSaveIfNeeded();
        }

        private void JobList_SelectedItemChanged(ListViewItemEventArgs obj)
        {
            SelectJob(obj.Item);
        }

        private void encryptButtonClick()
        {
            var encrypt = new EncryptDialog();
            Application.Run(encrypt);
            password.Text = encrypt.Result;
        }


        private void CreateJobButton_Clicked()
        {
            PromptForSaveIfNeeded();
            var new_job = new JobConfig() { Name = "<unnamed>" };
            current_config.Jobs.Add(new_job);
            
            // Refresh the list and select the new job
            var wrapper = new ListWrapper(current_config.Jobs);
            jobList.Source = wrapper;
            jobList.SelectedItem = current_config.Jobs.Count - 1;
            jobList.SetFocus();
        }
    }
}
