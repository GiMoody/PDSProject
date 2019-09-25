using System;

using System.Diagnostics;
using System.Linq;
using System.ComponentModel;
using System.Drawing;

using System.Collections.Generic;
using System.Collections.ObjectModel;

using System.IO;
using System.IO.Pipes;
using System.IO.Compression;

using System.Security.Cryptography;
using System.Security.Principal;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;


using System.Threading;
using System.Threading.Tasks;

using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;

using MessageBox  = System.Windows.MessageBox;
using ListBox     = System.Windows.Controls.ListBox;
using MenuItem    = System.Windows.Forms.MenuItem;
using Path        = System.IO.Path;
using ContextMenu = System.Windows.Forms.ContextMenu;
using Button      = System.Windows.Controls.Button;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace PDSProject {
    /// <summary>
    /// Interaction logic form MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {
        // Used as reference for the other classes
        public static MainWindow main;
        private string save_path;
        public ObservableCollection<FileRecive> fileReciveList = new ObservableCollection<FileRecive>();

        SharedInfo _referenceData = SharedInfo.Instance;
        MyTCPListener _TCPListener;
        MyTCPSender _TCPSender;
        MyUDPListener _UDPListener;
        MyUDPSender _UDPSender;
        CancellationTokenSource source;

        SemaphoreSlim obj = new SemaphoreSlim(0);
        
        // Initialization Timer for TrayIcon blink
        DispatcherTimer flashTimer = new DispatcherTimer();
        DispatcherTimer dispatcherTimer_CleanUp = new DispatcherTimer();
        DispatcherTimer dispatcherTimer_FileCleanUp = new DispatcherTimer();

        // Initialization contextMenù
        ContextMenu contextMenu;

        // Initialization notifyIcon 
        NotifyIcon ni = new NotifyIcon();
        private Icon[] icons;
        private int currentIcon;

        public MainWindow() {
            InitializeComponent();
            
            main = this;

            // Initialize network elements
            this._TCPListener = new MyTCPListener();
            this._TCPSender = new MyTCPSender();
            this._UDPListener = new MyUDPListener();
            this._UDPSender = new MyUDPSender();

            this.source = new CancellationTokenSource();

            // Initialize friendList data
            this.friendList.ItemsSource = _referenceData.GetHosts().Values;//.Users.Values;
           
            // Initialize contextMenu 
            this.contextMenu = new System.Windows.Forms.ContextMenu();
            System.Windows.Forms.MenuItem statusItem = new System.Windows.Forms.MenuItem("Status");
            statusItem.MenuItems.Add(0, new System.Windows.Forms.MenuItem("Online", new System.EventHandler(Status_Click)));
            statusItem.MenuItems.Add(1, new System.Windows.Forms.MenuItem("Offline", new System.EventHandler(Status_Click)));
            this.contextMenu.MenuItems.Add(0, statusItem);
            this.contextMenu.MenuItems.Add(1, new System.Windows.Forms.MenuItem("Show", new System.EventHandler(Show_Click)));
            this.contextMenu.MenuItems.Add(2, new System.Windows.Forms.MenuItem("Exit", new System.EventHandler(Exit_Click)));

            // Initialize localUser data
            InitLocalUserData();
            this.DataContext = fileReciveList;
            this.fileList.ItemContainerGenerator.StatusChanged += ItemContainerGeneratorStatusChanged;

            // ContextMenù to NotifyIcon
            this.ni.Visible = true;
            this.ni.ContextMenu = this.contextMenu;
            this.ni.Text = "PDS_Condividi";
            this.ni.BalloonTipClicked += new EventHandler(notifyIcon_BalloonTipClicked);
            this.ni.DoubleClick +=
                delegate ( object sender, EventArgs args ) {
                    this.Show();
                    this.WindowState = WindowState.Normal;
                };

            // Every second, a UDP packet is sent to notify other users of update of status, name or profile image
            this.dispatcherTimer_CleanUp = new DispatcherTimer();
            this.dispatcherTimer_CleanUp.Tick += new EventHandler(DispatcherTimer_Tick);
            this.dispatcherTimer_CleanUp.Interval = new TimeSpan(0, 0, 1);
            this.dispatcherTimer_CleanUp.Start();
            
            this.dispatcherTimer_FileCleanUp = new DispatcherTimer();
            this.dispatcherTimer_FileCleanUp.Tick += new EventHandler(DispatcherTimer_ClearFileList);
            this.dispatcherTimer_FileCleanUp.Interval = new TimeSpan(0, 0, 50);
            this.dispatcherTimer_FileCleanUp.Start();

            this.flashTimer.Tick += new EventHandler(IconBlinking);
            this.flashTimer.Interval = new TimeSpan(0, 0, 1);
            
            // Start UDP server
            Task.Run(async () => {
                try {
                    await _UDPListener.Listener(source.Token);
                }catch(Exception e) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - On _UDPListener Task. Exception {e.Message}");
                }
            });

            // Start Pipe client thread
            Task.Run(() => { PipeClient(); });
            
            // First start of listener
            this._referenceData.isFirst = true;
            StartTCPListener();

            // Initialize icons for status
            this.icons = new Icon[2];
            this.icons[0] = new System.Drawing.Icon(Utility.FileNameToSystem("share_green.ico"));
            this.icons[1] = new System.Drawing.Icon(Utility.FileNameToSystem("share_black.ico"));
        }

        /// <summary>
        /// During initialization phase, loads local user data on the layout
        /// </summary>
        protected void InitLocalUserData() {
            // Display local user information
            CurrentHostProfile currentLocalUser = _referenceData.GetInfoLocalUser();

            // Initialize icon
            ni.Icon = currentLocalUser.Status.Equals("online") == true ?
                      new System.Drawing.Icon(Utility.FileNameToSystem("share_green.ico")) :
                      new System.Drawing.Icon(Utility.FileNameToSystem("share_red.ico"));
            
            // Initialize local UserName
            textUserName.Text = currentLocalUser.Name;

            // Update status
            if (currentLocalUser.Status.Equals("online")) {
                comboStatus.Text        = "Online";
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("green_dot.png")));
            } else {
                comboStatus.Text        = "Offline";
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("red_dot.png")));
            }
            comboStatus.SelectedIndex = currentLocalUser.Status.ToString() == "online" ? 0 : 1;

            // Update SavePath & AcceptFileConfiguration
            if (currentLocalUser.SavePath.Equals(_referenceData.defaultPath)) {
                ChoosePath.IsChecked = true;
                pathName.Text        = _referenceData.defaultPath;
            } else {
                ChoosePath.IsChecked = false;
                pathName.Text        = currentLocalUser.SavePath;
            }
            AcceptFile.IsChecked = currentLocalUser.AcceptAllFile == true ? true : false;

            // Loading Profile Image, distinguishes defualt or not
            string filename = currentLocalUser.ProfileImagePath;

            if (currentLocalUser.ProfileImagePath.Equals(_referenceData.defaultImage))
                filename = Utility.FileNameToHost(_referenceData.defaultImage);

            ImageBrush imgBrush  = new ImageBrush();
            imgBrush.ImageSource = new BitmapImage(new Uri(filename));
            ImageProfile.Fill    = imgBrush;
        }

        #region --------------- NotifyIcon Settings ---------------
        /// <summary>
        /// Manage switch beetween colours of the NotifyIcon
        /// </summary>
        private void IconBlinking(object sender, EventArgs e) {
            ni.Icon = icons[currentIcon];
            if(currentIcon++ == 1) {
                currentIcon = 0;
            }
        }
        
        /// <summary>
        /// Manage the TaskBarIcon blink
        /// </summary>
        public void FlashingWindowIcon() {
            main.FlashWindow();
        }

        /// <summary>
        /// Manage icon blinking when a PopUp Balloon is created
        /// </summary>
        public void NotifySistem() {
            // Make icon taskbar flash
            main.FlashWindow();
            // Start timer for icon flashing in tray           
            flashTimer.Start();
        }

        /// <summary>
        /// Stop timer for taskbar icon
        /// </summary>
        public void StopNotify() {
            flashTimer.Stop();
            ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_green.ico"));
        }

        /// <summary>
        /// Manage MinimizeState (icon in tray)
        /// </summary>
        protected override void OnStateChanged(EventArgs e) {
            if(WindowState == System.Windows.WindowState.Minimized)
                Hide();
            base.OnStateChanged(e);
        }

        /// <summary>
        /// Gestione Click Ballon di notifica ricezione file
        /// </summary>
        void notifyIcon_BalloonTipClicked(object sender, EventArgs e) {
            // The MainWindow is setted as the foreground window
            Show();
            Activate();
            WindowState = WindowState.Normal;

            main.StopFlashingWindow();

            // Select the file of which I want a response
            FileRecive fileTAG = (FileRecive)((NotifyIcon)sender).Tag;
            fileList.SelectedItems.Add(fileTAG);

            if (!_referenceData.GetInfoLocalUser().AcceptAllFile){
                // Set as visible the Yes/No buttons of the selected element
                var currentSelectedListBoxItem = this.fileList.ItemContainerGenerator.ContainerFromIndex((int)fileList.Items.IndexOf(fileTAG)) as ListBoxItem;

                // Get all the buttons and set the visibility value
                Button yesButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "yesButton");
                Button noButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "noButton");
                Button stopButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "stopButton");

                yesButton.Visibility = Visibility.Visible;
                noButton.Visibility = Visibility.Visible;
                stopButton.Visibility = Visibility.Hidden;
            }
        }

        #endregion

        #region --------------- ContextMenù Events ----------------     

        /// <summary>
        /// Status changed from context menu
        /// </summary>
        protected void Status_Click(Object sender, System.EventArgs e) {
            MenuItem statItem = (MenuItem)sender;
            if(statItem.Text == "Online") {
                Console.WriteLine($"{DateTime.Now.ToString()}\t - Change status Local host to ONLINE");
                comboStatus.Text = "Online";
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("green_dot.png")));
                ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_green.ico"));

                _referenceData.UpdateStatusLocalUser("online");
            } else {
                Console.WriteLine($"{DateTime.Now.ToString()}\t - Change status Local host to OFFLINE");
                comboStatus.Text = "Offline";
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("red_dot.png")));
                ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_red.ico"));

                _referenceData.UpdateStatusLocalUser("offline");
            }
        }

        /// <summary>
        /// Set windows as foreground from context menu
        /// </summary>
        protected void Show_Click(Object sender, System.EventArgs e) {
            this.Show();
            this.WindowState = WindowState.Normal;
        }

        /// <summary>
        /// Exit application from context menu
        /// </summary>
        protected void Exit_Click(Object sender, System.EventArgs e) {
            Close();
        }
    
        #endregion

        #region --------------- HomePage Events -------------------
        /// <summary>
        /// Set windows "Setting" as Visible 
        /// </summary>
        private void Settings_visible(object sender, MouseButtonEventArgs e) {
            SettingsCanvas.Visibility = Visibility.Visible;
            MainCanvas.Visibility = Visibility.Hidden;

            CurrentHostProfile currentLocalHost = _referenceData.GetInfoLocalUser();

            // Upload profile image
            string filename = currentLocalHost.ProfileImagePath;
            if(currentLocalHost.ProfileImagePath.Equals(_referenceData.defaultImage))
                filename = Utility.FileNameToHost(_referenceData.defaultImage);
            
            ImageBrush imgBrush = new ImageBrush();
            imgBrush.ImageSource = new BitmapImage(new Uri(filename));
            ImageSettingsProfile.Fill = imgBrush;

            // Upload the most recent username
            textChangeName.Text = currentLocalHost.Name;
            AcceptFile.IsChecked = currentLocalHost.AcceptAllFile;

            if(currentLocalHost.SavePath.Equals(_referenceData.defaultPath)) {
                ChoosePath.IsChecked = true;
                pathName.Text = _referenceData.defaultPath;
            } else {
                ChoosePath.IsChecked = false;
                pathName.Text = currentLocalHost.SavePath;
            }
        }
        
        /// <summary>
        /// Combobox Status changed
        /// </summary>
        private void ComboStatus_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
            if(((ComboBoxItem)e.AddedItems[0]).Content.ToString().Equals("Online")) {
                _referenceData.UpdateStatusLocalUser("online");
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("green_dot.png")));
                ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_green.ico"));

            } else if(((ComboBoxItem)e.AddedItems[0]).Content.ToString().Equals("Offline")) {
                _referenceData.UpdateStatusLocalUser("offline");
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("red_dot.png")));
                ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_red.ico"));
            }
        }

        /// <summary>
        /// Prepare files for sending and send SEND_REQUEST messages
        /// </summary>
        private async void ButtonSend_Click(object sender, RoutedEventArgs e) {
            List<string> currentSelectedHost = _referenceData.GetCurrentSelectedHost();
            List<string> currentPathToSend = _referenceData.GetPathFileToSend();

            // Check if at least one file and host is selected 
            if(currentPathToSend.Count > 0 && currentSelectedHost.Count > 0) {

                List<string> pathFiles = new List<string>();
                Dictionary<string, FileSendStatus> listFile = new Dictionary<string, FileSendStatus>();

                // Zip operation executed on another thread using a Task
                await Task.Run(() => {
                    try {
                        // Set the zip file name : timestamp(ms)Files_IPUser_.zip
                        string zipPath = @Utility.PathTmp() + "\\" + DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString() + "Files_" + _referenceData.GetLocalIPAddress().Replace(".", "_") + "_.zip";
                        bool isFile = false;

                        // For each file selected
                        foreach(string path in currentPathToSend) {
                            FileAttributes fileAttributes = File.GetAttributes(path);
                            
                            if(fileAttributes.HasFlag(FileAttributes.Directory)) {
                                // If is a directory create another zip
                                // Zip file name for directory : timestamp(ms)Dir_IPUser_.zip
                                string zipPathDir = @Utility.PathTmp() + "\\" + DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString() + "Dir_" + Path.GetFileName(path) + "_" + _referenceData.GetLocalIPAddress().Replace(".", "_") + "_.zip";

                                // Add as request file for all the selected user
                                listFile.Add(Utility.PathToFileName(zipPathDir), FileSendStatus.PREPARED);
                                foreach (string ip in currentSelectedHost) {
                                    Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                                        AddOrUpdateListFile(ip, Utility.PathToFileName(zipPathDir), FileSendStatus.PREPARED, "", 0.0f);
                                    }));
                                }

                                ZipFile.CreateFromDirectory(path, zipPathDir);
                                
                                // Update status recived file after at the end...
                                listFile[Utility.PathToFileName(zipPathDir)] = FileSendStatus.READY;
                                foreach (string ip in currentSelectedHost) {
                                    Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                                        MainWindow.main.AddOrUpdateListFile(ip, Utility.PathToFileName(zipPathDir), FileSendStatus.READY, "", 0.0f);
                                    })); 
                                }
                            } else {
                                // In case is a list of file use the name created before
                                if(!File.Exists(zipPath)) {
                                    // If the file doesn't exist, create it and add the first file (also set data in the correct collection)
                                    listFile.Add(Utility.PathToFileName(zipPath), FileSendStatus.PREPARED);
                                    foreach (string ip in currentSelectedHost) {
                                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                                            AddOrUpdateListFile(ip, Utility.PathToFileName(zipPath), FileSendStatus.PREPARED, "", 0.0f);
                                        }));
                                    }

                                    using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create)) {
                                        archive.CreateEntryFromFile(path, Utility.PathToFileName(path));
                                        isFile = true;
                                    }
                                } else {
                                    using(ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Update)) {
                                        archive.CreateEntryFromFile(path, Utility.PathToFileName(path));
                                    }
                                }
                            }

                        }

                        // In case a file zip exists it set the status of the file inside the collection as ready
                        if (isFile) {
                            listFile[Utility.PathToFileName(zipPath)] = FileSendStatus.READY;

                            // Update the listbox for file
                            foreach (string ip in currentSelectedHost) {
                                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                                    AddOrUpdateListFile(ip, Utility.PathToFileName(zipPath), FileSendStatus.READY, "", 0.0f);
                                }));                                
                            }
                        }

                        foreach (string ip in currentSelectedHost) {
                            _referenceData.AddOrUpdateSendFile(ip, listFile);
                            foreach (string file in listFile.Keys) {
                                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                                    AddOrUpdateListFile(ip, Utility.PathToFileName(file), FileSendStatus.READY, "", 0.0f);
                                }));
                            }
                        }

                        // Get the list of file name and clean the current selected file
                        pathFiles = listFile.Keys.ToList();
                        _referenceData.ClearPathToSend(currentPathToSend);
                        obj.Release();
                    } catch(Exception ex) {
                        Console.WriteLine($"{DateTime.Now.ToString()}\t - Exception on creation zip {ex.Message}");

                        // Delete all file in case of exception
                        foreach(string file in listFile.Keys) {
                            string fullPath = Utility.PathTmp() + "\\" + file;
                            if (File.Exists(fullPath)) {
                                File.Delete(fullPath);
                            }
                        }
                        // Release the semaphore to unlock the main thread
                        obj.Release();
                    }
                });
                // Clean the selected host list
                _referenceData.RemoveSelectedHosts(currentSelectedHost);
                textInfoMessage.Text = "";
                UndoButton.Visibility = Visibility.Hidden;
                await obj.WaitAsync();
                try {
                    // Send all the requests
                    await _TCPSender.SendRequest(pathFiles);
                } catch(Exception ex) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - Exception on ButtonSend_Click {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Undo selection sending files
        /// </summary>
        private void ButtonUndo_Click(object sender, RoutedEventArgs e) {
            _referenceData.ClearPathFileToSend();
            textInfoMessage.Text = "";
            UndoButton.Visibility = Visibility.Hidden;
        }

        #endregion

        #region --------------- Settings Events -------------------
        /// <summary>
        /// Update Profile Image
        /// </summary>
        private void Image_MouseDown(object sender, MouseButtonEventArgs e) {
            // Base windows tool for selecting a file in the filesystem
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();

            dlg.DefaultExt = ".png";
            dlg.Filter = "Image files(*.jpg, *.jpeg, *.jpe, *.jfif, *.png) | *.jpg; *.jpeg; *.jpe; *.jfif; *.png";

            // ShowDialog shows the OpenFileDialog window
            Nullable<bool> result = dlg.ShowDialog();

            if(result == true) {
                // Change the profile image (and upload it) only if the hash is different 
                string filename = dlg.FileName;
                using(SHA256 sha = SHA256.Create()) {
                    FileStream file = File.OpenRead(filename);
                    byte[] hash = sha.ComputeHash(file);
                    if(!BitConverter.ToString(hash).Replace("-", String.Empty)
                        .Equals(_referenceData.GetInfoLocalUser().ProfileImageHash)) {
                        ImageBrush imgBrush = new ImageBrush();
                        imgBrush.ImageSource = new BitmapImage(new Uri(filename));

                        ImageProfile.Fill = imgBrush;
                        ImageSettingsProfile.Fill = imgBrush;
                    }
                }
            }
        }

        /// <summary>
        /// Main Visible
        /// </summary>
        private void BackButton_OnClick(object sender, RoutedEventArgs e) {
            SettingsCanvas.Visibility = Visibility.Hidden;
            MainCanvas.Visibility = Visibility.Visible;

            CurrentHostProfile currentLocalHost = _referenceData.GetInfoLocalUser();

            // Reset the previous profile image
            string filename = currentLocalHost.ProfileImagePath;
            if(currentLocalHost.ProfileImagePath.Equals(_referenceData.defaultImage))
                filename = Utility.FileNameToHost(_referenceData.defaultImage);
            
            ImageBrush imgBrush = new ImageBrush();
            imgBrush.ImageSource = new BitmapImage(new Uri(filename));
            ImageProfile.Fill = imgBrush;
        }

        /// <summary>
        /// Update user changes
        /// </summary>
        private async void ApplyButton_OnClick(object sender, RoutedEventArgs e) {
            if(this.textChangeName.Text == "") {
                string messageWarning           = "Inserire Username";
                string caption_warning          = "Attenzione";
                MessageBoxImage icon_warning    = MessageBoxImage.Information;
                MessageBoxButton button_warning = MessageBoxButton.OK;

                MessageBox.Show(messageWarning, caption_warning, button_warning, icon_warning);
            } else {
                // Configure the message box to be displayed
                string messageBoxText   = "Modifiche Applicate";
                string caption          = "Attenzione";
                MessageBoxImage icon    = MessageBoxImage.Information;
                MessageBoxButton button = MessageBoxButton.OK;

                MessageBox.Show(messageBoxText, caption, button, icon);

                try {
                    string pathProfileImage = ((BitmapImage)((ImageBrush)ImageSettingsProfile.Fill).ImageSource).UriSource.OriginalString.ToString();
                    using(SHA256 sha = SHA256.Create()) {
                        FileStream file = File.OpenRead(pathProfileImage);
                        byte[] hash     = sha.ComputeHash(file);

                        if(!BitConverter.ToString(hash).Replace("-", string.Empty).Equals(_referenceData.GetInfoLocalUser().ProfileImageHash)) {
                            ImageBrush imgBrush  = new ImageBrush();
                            imgBrush.ImageSource = new BitmapImage(new Uri(pathProfileImage));

                            ImageProfile.Fill         = imgBrush;
                            ImageSettingsProfile.Fill = imgBrush;
                        }
                    }
                    _referenceData.UpdateInfoLocalUser(textChangeName.Text, pathProfileImage, pathName.Text, ChoosePath.IsChecked, AcceptFile.IsChecked);
                    textUserName.Text = textChangeName.Text;

                    if(_referenceData.GetOnlineUsers().Count > 0)
                        await _TCPSender.SendProfilePicture();
                } catch(Exception ex) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - Something went wrong updating user information. Maybe the data are not corrected or unknown exception occured {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Update saving path
        /// </summary>
        private void CheckBox_Uncheck(object sender, RoutedEventArgs e) {
            folderButton.Visibility = Visibility.Visible;
            pathName.IsReadOnly = false;
            pathName.Background = new SolidColorBrush(Colors.White);
            pathName.Text = _referenceData.GetInfoLocalUser().SavePath;
        }

        /// <summary>
        /// Default Saving Path
        /// </summary>
        private void CheckBox_Check(object sender, RoutedEventArgs e) {
            folderButton.Visibility = Visibility.Hidden;
            pathName.IsReadOnly = true;
            pathName.Background = new SolidColorBrush(Colors.LightGray);
            pathName.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        /// <summary>
        /// Button Customized Path
        /// </summary>
        private void FolderButton_OnClick(object sender, RoutedEventArgs e) {
            var save_dlg = new CommonOpenFileDialog();
            save_dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            save_dlg.IsFolderPicker = true;

            CommonFileDialogResult result = save_dlg.ShowDialog();
            if(result.ToString() == "Ok") {
                save_path = save_dlg.FileName;
                pathName.Text = save_path;
            }
        }

        /// <summary>
        /// Update GUI that show the files/directory that the local user wants to send
        /// </summary>
        /// <param name="path">Path of the file/directory</param>
        public void ShowCurrentListSendFile ( string path ) {
            textInfoMessage.Text += path + "\n";
            if (_referenceData.GetPathFileToSend().Count > 0) {
                UndoButton.Visibility = Visibility.Visible;
            }
        }
        #endregion

        #region --------------- Window Events ---------------------
        /// <summary>
        /// Overload OnClosing Event (messageBox)
        /// </summary>
        private void MainWindow_Closing(object sender, CancelEventArgs e) {
            // Configure the message box to be displayed
            string messageBoxText = "Vuoi chiudere l'applicazione?\n(Cliccando ''NO'' rimarrà attiva in basso)";
            string caption = "Attenzione";
            MessageBoxButton button = MessageBoxButton.YesNo;
            MessageBoxImage icon = MessageBoxImage.Question;

            // Display message box
            MessageBoxResult result = MessageBox.Show(messageBoxText, caption, button, icon);

            switch(result) {
                case MessageBoxResult.Yes:
                    e.Cancel = false;
                    dispatcherTimer_CleanUp.Stop();
                    dispatcherTimer_FileCleanUp.Stop();
                    flashTimer.Stop();

                    if (Directory.GetFiles(Utility.PathTmp()).Count() > 1) {
                        foreach (string file in Directory.GetFiles(Utility.PathTmp())) {
                            string name = Utility.PathToFileName(file);
                            if (name.Equals("README.txt")) { continue; }

                            try {
                                File.Delete(file);
                            }
                            catch (IOException exp) {
                                Console.WriteLine($"{DateTime.Now.ToString()}\t - Exception during cleaning temp file: {exp.Message}");
                            }
                        }
                    }

                    source.Cancel();
                    _TCPListener.StopServer();
                break;
                case MessageBoxResult.No:
                    this.WindowState = WindowState.Minimized;
                    e.Cancel = true;
                break;
            }

        }
        
        #endregion

        #region --------------- File ListBox ----------------------
        /// <summary>
        /// Update fileReciveList obj for file list recived
        /// </summary>
        /// <param name="ipUser">User's ip</param>
        /// <param name="pathFile">Filename to add or update</param>
        /// <param name="status">Status to add or update</param>
        /// <param name="estimatedTime">Estimated time to recive/send file</param>
        /// <param name="byteReceived">Number of byte received/sended</param>
        public void AddOrUpdateListFile(string ipUser, string pathFile, FileRecvStatus? status, string estimatedTime, double? byteReceived){
            // If the file already exists in the collection... update it
            if (fileReciveList.Where(e => e.fileName.Equals(pathFile)).Count() > 0) {
                for (int i = 0; i < fileReciveList.Count; i++) {
                    if (fileReciveList[i].fileName.Equals(pathFile)) {
                        if (status != null) {
                            fileReciveList[i].UpdateStatusString(status.Value);
                            if((status.Value == FileRecvStatus.NSEND) || (status.Value == FileRecvStatus.RECIVED)) {
                                
                                int index = (int)fileList.Items.IndexOf(GetFileReciveByFileName(pathFile));
                                var currentSelectedListBoxItem = this.fileList.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem;

                                if(currentSelectedListBoxItem == null) {
                                    fileList.UpdateLayout();
                                    fileList.ScrollIntoView(fileList.Items[index]);
                                    currentSelectedListBoxItem = this.fileList.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem;
                                }
                                Button stopButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "stopButton");
                                stopButton.Visibility = Visibility.Hidden;  
                            }
                        }
                        if (estimatedTime != null)
                            fileReciveList[i].estimatedTime = estimatedTime;
                        if (byteReceived != null)
                            fileReciveList[i].dataRecived = byteReceived.Value;
                        break;
                    }
                }
            } else {
                // ...otherwise add it
                string currentUsername = "Da: " + _referenceData.GetRemoteUserName(ipUser);
                FileRecive files = new FileRecive(currentUsername, pathFile, status.Value, "0", 0);

                files.ip = ipUser;
                fileReciveList.Add(files);              
            }
        }

        /// <summary>
        /// Update fileReciveList obj for file list sended
        /// </summary>
        /// <param name="ipUser">User's ip</param>
        /// <param name="pathFile">Filename to add or update</param>
        /// <param name="status">Status to add or update</param>
        /// <param name="estimatedTime">Estimated time to recive/send file</param>
        /// <param name="byteReceived">Number of byte received/sended</param>
        public void AddOrUpdateListFile ( string ipUser, string pathFile, FileSendStatus? status, string estimatedTime, double? byteReceived ) {
            // If the file already exists in the collection... update it
            if (fileReciveList.Where(e => e.fileName.Equals(pathFile)).Count() > 0) {
                for (int i = 0; i < fileReciveList.Count; i++) {
                    if (fileReciveList[i].fileName.Equals(pathFile)) {
                        if (status != null) {
                            fileReciveList[i].UpdateStatusString(status.Value);
                            if ((status.Value == FileSendStatus.REJECTED) || (status.Value == FileSendStatus.END)) {

                                int index = (int)fileList.Items.IndexOf(GetFileReciveByFileName(pathFile));
                                var currentSelectedListBoxItem = this.fileList.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem;

                                if (currentSelectedListBoxItem == null) {
                                    fileList.UpdateLayout();
                                    fileList.ScrollIntoView(fileList.Items[index]);
                                    currentSelectedListBoxItem = this.fileList.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem;
                                }

                                Button stopButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "stopButton");
                                stopButton.Visibility = Visibility.Hidden;
                            }
                            if(status.Value == FileSendStatus.RESENT) {
                                fileReciveList[i].TimestampResend = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            }
                        }

                        if (estimatedTime != null)
                            fileReciveList[i].estimatedTime = estimatedTime;
                        if (byteReceived != null)
                            fileReciveList[i].dataRecived = byteReceived.Value;
                        break;
                    }

                }
            }
            else {
                // ...otherwise add it
                string currentUsername = "A: " + _referenceData.GetRemoteUserName(ipUser);
                FileRecive files = new FileRecive(currentUsername, pathFile, status.Value, "0", 0);
                files.ip = ipUser;
                fileReciveList.Add(files);
            }
        }


        /// <summary>
        /// Update inside the listbox the username of the remote hosts
        /// </summary>
        /// <param name="ipUser"></param>
        /// <param name="newName"></param>
        public void UpdateHostName(string ipUser, string newName ) {
            foreach(FileRecive fr in fileReciveList) {
                if (fr.ip.Equals(ipUser)) {
                    if (fr.isRecived) {
                        fr.hostName = "Da: " + newName;
                    } else {
                        fr.hostName = "A: " + newName;
                    }
                }
            }
        }

        /// <summary>
        /// Overload ItemContainerGenerator method
        /// </summary>
        private void ItemContainerGeneratorStatusChanged(object sender, EventArgs e) {
            if(fileList.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated) {
                var containers = fileList.Items.Cast<object>().Select(item => (FrameworkElement)fileList.ItemContainerGenerator.ContainerFromItem(item));

                foreach(var container in containers) {
                    if(container != null) {
                        container.Loaded += ItemContainerLoaded;
                    }
                }
            }
        }

        /// <summary>
        /// Manage the generation of the PopUp balloon and the update of File_Listbox's buttons
        /// </summary>
        private void ItemContainerLoaded(object sender, RoutedEventArgs e) {
            var element = (FrameworkElement)sender;
            element.Loaded -= ItemContainerLoaded;

            // Update data inside the listbox to show the correct set of buttons
            ListBoxItem fr = sender as ListBoxItem;
            FileRecive fr_listbox = fr.DataContext as FileRecive;

            string title_ball = "PDS_Condividi";
            string text_ball = "Utente " + fr_listbox.hostName + " ti vuole inviare un file!";
            int index = (int)fileList.Items.IndexOf(GetFileReciveByFileName(fr_listbox.fileName));
            var currentSelectedListBoxItem = fileList.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem;

            if(currentSelectedListBoxItem == null) {
                fileList.UpdateLayout();
                fileList.ScrollIntoView(fileList.Items[index]);
                currentSelectedListBoxItem = fileList.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem;
            }

            Button yesButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "yesButton");
            Button noButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "noButton");
            Button stopButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "stopButton");

            if (fr_listbox.isRecived) {
                if (_referenceData.GetInfoLocalUser().AcceptAllFile) {
                    yesButton.Visibility = Visibility.Hidden;
                    noButton.Visibility = Visibility.Hidden;
                    stopButton.Visibility = Visibility.Visible;
                }
                else {
                    yesButton.Visibility = Visibility.Visible;
                    noButton.Visibility = Visibility.Visible;
                    stopButton.Visibility = Visibility.Hidden;
                }

                ni.ShowBalloonTip(5, title_ball, text_ball, ToolTipIcon.Info);
                ni.Tag = GetFileReciveByFileName(fr_listbox.fileName);
            }
            else {
                TextBlock textTime = MainWindow.FindChild<TextBlock>(currentSelectedListBoxItem, "textTime");
                ProgressBar progressFile = MainWindow.FindChild<ProgressBar>(currentSelectedListBoxItem, "progressFile");

                yesButton.Visibility = Visibility.Hidden;
                noButton.Visibility = Visibility.Hidden;
                stopButton.Visibility = Visibility.Visible;
                textTime.Visibility = Visibility.Visible;
                progressFile.Visibility = Visibility.Visible;

            }
        }

        /// <summary>
        /// Manage the YES_Button interaction in the listbox
        /// </summary>
        private void YesButton_Click(object sender, RoutedEventArgs e) {
            Button button = sender as Button;
            var index = fileList.Items.IndexOf(button.Tag);

            var currentSelectedListBoxItem = this.fileList.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem;

            Button yesButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "yesButton");
            Button noButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "noButton");
            Button stopButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "stopButton");

            TextBlock textFile = MainWindow.FindChild<TextBlock>(currentSelectedListBoxItem, "textFile");
            string fileName = textFile.Text;
            string[] packetPart = fileName.Split('_');
            string IpTAG = packetPart[packetPart.Length - 5] + "." + packetPart[packetPart.Length - 4] + "." + packetPart[packetPart.Length - 3] + "." + packetPart[packetPart.Length - 2];

            SendResponse(fileName, IpTAG, PacketType.YFILE);

            yesButton.Visibility = Visibility.Hidden;
            noButton.Visibility = Visibility.Hidden;
            stopButton.Visibility = Visibility.Visible;

            StopNotify();
        }
        
        /// <summary>
        /// Manage the NO_Button or X_Button interaction in the listbox
        /// </summary>
        private void NoButton_Click(object sender, RoutedEventArgs e) {

            Button button = sender as Button;
            var index = fileList.Items.IndexOf(button.Tag);

            var currentSelectedListBoxItem = this.fileList.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem;

            Button yesButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "yesButton");
            Button noButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "noButton");
            Button stopButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "stopButton");

            TextBlock textFile = MainWindow.FindChild<TextBlock>(currentSelectedListBoxItem, "textFile");
            string fileName = textFile.Text;
            string[] packetPart = fileName.Split('_');
            string IpTAG = packetPart[packetPart.Length - 5] + "." + packetPart[packetPart.Length - 4] + "." + packetPart[packetPart.Length - 3] + "." + packetPart[packetPart.Length - 2];

            if(((FileRecive)button.Tag).isRecived) {
                SendResponse(fileName, IpTAG, PacketType.NFILE);
            } else {
                AddOrUpdateListFile(((FileRecive)button.Tag).ip, fileName, FileSendStatus.REJECTED, "-", 0);
                _referenceData.UpdateSendStatusFileForUser(((FileRecive)button.Tag).ip, fileName, FileSendStatus.REJECTED);
            }
                
            yesButton.Visibility = Visibility.Hidden;
            noButton.Visibility = Visibility.Hidden;
            stopButton.Visibility = Visibility.Hidden;

            StopNotify();
        }

        /// <summary>
        /// Return the FileReceive object with file name equals to the argument
        /// </summary>
        /// <param name="fileName">filename to check</param>
        /// <returns>FileReceive if exists, null otherwise</returns>
        public FileRecive GetFileReciveByFileName(string fileName) {
            if(fileReciveList.Where(e => e.fileName.Equals(fileName)).Count() > 0) {
                return fileReciveList.Where(e => e.fileName.Equals(fileName)).ToList()[0];
            } else
                return null;
        }

        /// <summary>
        /// Send response to remote host to receive a file/directory previousply announced
        /// </summary>
        /// <param name="filename">Name of the file</param>
        /// <param name="ip">Remote host's ip</param>
        /// <param name="type">Type of response (yes/no)</param>
        public async void SendResponse ( string filename, string ip, PacketType type ) {
            try {
                FileRecvStatus status = type == PacketType.YFILE ? FileRecvStatus.YSEND : FileRecvStatus.NSEND;
                AddOrUpdateListFile(ip, filename, status, "-", 0.0f);
                await _TCPSender.SendResponse(ip, filename, type);
            }
            catch (Exception e) {
                Console.WriteLine($"{DateTime.Now.ToString()}\t - Exception on TestResponse Task - {e.Message}");
            }
        }
        #endregion

        #region --------------- Friends ListBox -------------------
        /// <summary>
        /// Manage selection in friend list
        /// </summary>
        private void FriendList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (e.AddedItems.Count > 0) {
                foreach (Host h in e.AddedItems) {
                    _referenceData.AddSelectedHost(h.Ip);

                    if (h.Status.Equals("offline")) {
                        friendList.SelectedItems.Remove(h);
                        _referenceData.RemoveSelectedHost(h.Ip);
                    }
                }
            }

            if (e.RemovedItems.Count > 0) {
                foreach(Host h in e.RemovedItems) {
                    _referenceData.RemoveSelectedHost(h.Ip);
                }
            }

            if (_referenceData.GetCurrentSelectedHost().Count > 0) {
                SendButton.Visibility = Visibility.Visible;
            } else {
                SendButton.Visibility = Visibility.Hidden;
            }
        }

        /// <summary>
        /// Deselection friend list
        /// </summary>
        private void FriendList_DoubleClick(object sender, MouseButtonEventArgs e) {
            ListBox list = (ListBox)sender;
            if(list.SelectedItems.Count == 1) {
                friendList.SelectedIndex = -1;
                SendButton.Visibility = Visibility.Hidden;
            }
        }
        #endregion

        #region --------------- System Management -----------------
        /// <summary>
        /// NamedPipeClient methods
        /// Receive the path from another process intances that work as server
        /// </summary>
        private void PipeClient() {
            while (true) {
                using(NamedPipeClientStream pipeClient =
                    new NamedPipeClientStream(".", "PSDPipe", PipeDirection.In)) {
                    pipeClient.Connect();

                    using (StreamReader sr = new StreamReader(pipeClient)) {
                        string path;
                        while((path = sr.ReadLine()) != null) {
                            Console.WriteLine($"{DateTime.Now.ToString()}\t - Received from Pipe Server: {0}", path);
                            _referenceData.AddPathToSend(path);
                            string copia = path;

                            // Update Gui with path of the files/directory to be sended
                            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                                ShowCurrentListSendFile(copia);
                                //if (WindowState == System.Windows.WindowState.Minimized) {
                                    Show();
                                    Activate();
                                    WindowState = WindowState.Normal;
                                //}
                            }));

                        }
                    }
                }
            }
        }

        /// <summary>
        /// Starts current TCPListener
        /// </summary>
        public void StartTCPListener() {
            CancellationToken token = source.Token;
            Task.Run(async () => {
                try {
                    await _TCPListener.Listener(token);
                } catch(Exception e) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - Exception on StartTCPListener Task - {e.Message}");
                }
            });
        }

        /// <summary>
        /// Stops current TCPListener
        /// </summary>
        public void StopTCPListener() {
            _TCPListener.StopServer();
        }

        /// <summary>
        /// Called after confirmation receiving file
        /// </summary>
        /// <param name="filename">filename</param>
        /// <param name="ip">Sender's ip </param>
        public async void SendFile(string filename, string ip) {
            try {
                await _TCPSender.SendFile(Utility.PathToFileName(filename), ip);
            } catch(Exception e) {
                Console.WriteLine($"{DateTime.Now.ToString()}\t - Exception on SendFile Task - {e.Message}");
            }
        }

        /// <summary>
        /// DispatcherTimer method that send each second a UDP packet with the relevant the local user data to the other remote hots
        /// It also execute some cleaning/other timing operations
        /// </summary>
        private void DispatcherTimer_Tick(object sender, EventArgs e) {
            if(!_referenceData.CheckIfConfigurationIsSet()) {
                // If the network is not configurated yet, send the UDP packets to all the address related 
                // to all the network interfaces of the sysyem
                foreach(string ip in _referenceData.GetListBroadcastIps()) {
                    _UDPSender.Sender(ip);
                }
                friendList.Items.Refresh();
            } else {
                // In case the network has a configuration, send the UDP packet only to that interface
                _UDPSender.Sender(_referenceData.GetBroadcastIPAddress());

                // Check if a user is offline after not receiving a packet after 10 seconds
                long currentTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                foreach(Host u in _referenceData.GetOnlineUsers()) {
                    // Check if the last packet is received before 10 seconds...
                    if ((currentTime - u.LastPacketTime) > 10000) {
                        // ... if not set its status as offline
                        _referenceData.UpdateStatusUser(u.Ip, "offline");
                        UpdateProfileHost(u.Ip);
                        _referenceData.GetRecvFileIP(u.Ip);

                        // Set all the "inprogress" file as rejected
                        List<string> listCurrentRecvFile = _referenceData.GetListRecvFileIP(u.Ip);
                        foreach (string file in listCurrentRecvFile) {
                            if(_referenceData.CheckRecvFileStatus(u.Ip, file, FileRecvStatus.INPROGRESS)) {
                                _referenceData.UpdateStatusRecvFileForUser(u.Ip, file, FileRecvStatus.NSEND);
                                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                                    AddOrUpdateListFile(u.Ip, file, FileRecvStatus.NSEND, "", 0.0f);
                                }));
                            }
                        }
                    }
                }

                // Clean temp files
                if(Directory.GetFiles(Utility.PathTmp()).Count() > 1) {
                    foreach(string file in Directory.GetFiles(Utility.PathTmp())) {
                        string name = Utility.PathToFileName(file);
                        if(name.Equals("README.txt")) { continue; }

                        // Clear all the sended temp file
                        if(_referenceData.FileSendForAllUsers(name)) {
                            Console.WriteLine($"{DateTime.Now.ToString()}\t - Cancellazione file... {Utility.PathToFileName(file)}");
                            try {
                                File.Delete(file);
                                _referenceData.RemoveSendFile(file);
                            } catch(IOException exp) {
                                Console.WriteLine($"{DateTime.Now.ToString()}\t - Exception during cleaning temp file: {exp.Message}");
                            }
                        }

                        // Clear all the received temp file
                        if (_referenceData.FileReceiveFromAllUsers(name)) {
                            Console.WriteLine($"{DateTime.Now.ToString()}\t - Cancellazione file... {Utility.PathToFileName(file)}");
                            try {
                                File.Delete(file);
                                _referenceData.RemoveRecvFile(name);
                            } catch(IOException exp) {
                                Console.WriteLine($"{DateTime.Now.ToString()}\t - Exception during cleaning temp file: {exp.Message}");
                            }
                        }
                    }
                }
            }

            // Check which file need to be resent
            List<FileRecive> listResent;
            if((listResent = fileReciveList.Where(f => f.statusFile.Equals("Da rinviare")).ToList()).Count > 0) {
                FileRecive fr = listResent.OrderByDescending(f => f.TimestampResend).First();
                if (_referenceData.GetUserStatus(fr.ip).Equals("online")) {
                    _referenceData.UpdateSendStatusFileForUser(fr.ip, fr.fileName, FileSendStatus.CONFERMED);
                    Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                        AddOrUpdateListFile(fr.ip, fr.fileName, FileSendStatus.CONFERMED, "", 0.0f);
                        SendFile(fr.fileName, fr.ip);
                    }));
                }
            }

            // Check for which file need to resend the request
            if ((listResent = fileReciveList.Where(f => f.statusFile.Equals("Pronto per l'invio")).ToList()).Count > 0) {
                FileRecive fr = listResent.First();
                if (_referenceData.GetUserStatus(fr.ip).Equals("online")) {
                    Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () => {
                        await _TCPSender.SendRequest(new List<string> { fr.fileName });
                    }));
                }
            }

            // Stop the flashing icon if the windows is active
            if (IsActive == true) {
                main.StopFlashingWindow();
            }
        }

        /// <summary>
        /// Clear the listbox for all the rejected, ended and received file
        /// </summary>
        private void DispatcherTimer_ClearFileList ( object sender, EventArgs e ) {
            for (int i=0; i<fileReciveList.Count; i++) {
                if(fileReciveList[i].statusFile.Equals("Annullato") ||
                   fileReciveList[i].statusFile.Equals("Ricevuto")  ||
                   fileReciveList[i].statusFile.Equals("Fine invio") ) {
                    fileReciveList.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Update info Host through his IP
        /// </summary>
        public void UpdateProfileHost(string ip) {
            // Obtain the path of the profile image
            string filename = _referenceData.GetPathProfileImageHost(ip);

            // If the file doesn't exist return
            if(filename.Equals(""))
                return;

            try {
                var file = File.OpenRead(filename);
                file.Close();

                // Save the list of the current selected hosts
                List<string> lista = new List<string>();
                foreach(var item in friendList.SelectedItems)
                    lista.Add(((Host)item).Ip);

                // Refresh it...
                friendList.Items.Refresh();
                friendList.SelectedIndex = -1;

                // Reset the list
                foreach(var item in friendList.Items) {
                    if(lista.Contains(((Host)item).Ip) && ((Host)item).Status.Equals("online"))
                        friendList.SelectedItems.Add(item);
                }

            } catch(UnauthorizedAccessException e) {
                Console.WriteLine($"{DateTime.Now.ToString()}\t - File not yet reciced : {e.Message}");
            } catch(Exception e) {
                Console.WriteLine($"{DateTime.Now.ToString()}\t - Exception on UpdateProfileHost - {e.Message}");
            }
        }

        /// <summary>
        /// Sends ProfileImage
        /// </summary>
        public async void SendProfileImage() {
            try {
                await _TCPSender.SendProfilePicture();
            } catch(Exception e) {
                Console.WriteLine($"{DateTime.Now.ToString()}\t - Exception on SendProfileImage - {e.Message}");
            }
        }

        #endregion
        
        /// <summary>
        /// Finds a Child of a given item in the visual tree. 
        /// </summary>
        /// <param name="parent">A direct parent of the queried item.</param>
        /// <typeparam name="T">The type of the queried item.</typeparam>
        /// <param name="childName">x:Name or Name of child. </param>
        /// <returns>The first parent item that matches the submitted type parameter. 
        /// If not matching item can be found, 
        /// a null parent is being returned.</returns>
        public static T FindChild<T>(DependencyObject parent, string childName)
           where T : DependencyObject {
            // Confirm parent and childName are valid. 
            if(parent == null)
                return null;

            T foundChild = null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for(int i = 0; i < childrenCount; i++) {
                var child = VisualTreeHelper.GetChild(parent, i);
                // If the child is not of the request child type child
                T childType = child as T;
                if(childType == null) {
                    // recursively drill down the tree
                    foundChild = FindChild<T>(child, childName);

                    // If the child is found, break so we do not overwrite the found child. 
                    if(foundChild != null)
                        break;
                } else if(!string.IsNullOrEmpty(childName)) {
                    var frameworkElement = child as FrameworkElement;
                    // If the child's name is set for search
                    if(frameworkElement != null && frameworkElement.Name == childName) {
                        // if the child's name is of the request name
                        foundChild = (T)child;
                        break;
                    }
                } else {
                    // child element found.
                    foundChild = (T)child;
                    break;
                }
            }

            return foundChild;
        }

    }

}