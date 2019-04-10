using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO.Compression;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using Application = System.Windows.Application;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;
using System.Threading;
using ListBox = System.Windows.Controls.ListBox;
using Menu = System.Windows.Forms.Menu;
using MenuItem = System.Windows.Forms.MenuItem;
using UserControl = System.Windows.Controls.UserControl;

namespace PDSProject {
    /// <summary>
    /// Logica di interazione per MainWindow.xaml
    /// </summary>

    public partial class MainWindow : Window {

        //bool stat_online = false;
        private string save_path;

        public static MainWindow main; // Usato come riferimento da parte delle altri classi
        SharedInfo _referenceData = SharedInfo.Instance;
        MyTCPListener _TCPListener;
        MyTCPSender _TCPSender;
        MyUDPListener _UDPListener;
        MyUDPSender _UDPSender;
        CancellationTokenSource source;

        private System.Windows.Forms.ContextMenu contextMenu;
        
        System.Windows.Forms.NotifyIcon ni = new System.Windows.Forms.NotifyIcon();

        public MainWindow() {

            InitializeComponent();
            ChoosePath.IsChecked = true;
            pathName.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (_referenceData.LocalUser.Status.ToString() == "online")
            {
                comboStatus.SelectedIndex = 0;
            }
            else
            {
                comboStatus.SelectedIndex = 1;
            }
            main = this;
            _TCPListener = new MyTCPListener();
            _TCPSender = new MyTCPSender();
            _UDPListener = new MyUDPListener();
            _UDPSender = new MyUDPSender();

            source = new CancellationTokenSource();
            /*
            string zipPath = @Utility.PathTmp() + "\\" + DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString() + ".zip";
            using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                List<string> files = new List<string>();
                files.Add(@"C:\Users\chaks\Pictures\Saved Pictures\0IfKD81.png");
                files.Add(@"C:\Users\chaks\Pictures\Saved Pictures\1ee3d158c4913c5f4d7e505f3e436f89.jpg");
                foreach (string path in files)
                    archive.CreateEntryFromFile(path, Utility.PathToFileName(path));
            }
            */

            //initialize listBox
            //ObservableCollection<Host> items = new ObservableCollection<Host>();
            //items.Add(new Host() { Name = "Giulia", Status = "Online", ProfileImageHash = "", ProfileImagePath = ""});
            //items.Add(new Host() { Name = "Rossella", Status = "Offline", ProfileImageHash = "", ProfileImagePath = ""});
            //items.Add(new TodoItem() { Title = "Wash the car", Completion = 0 });

            //friendList.ItemsSource = items;
            friendList.ItemsSource = _referenceData.Users.Values;
           
            // Initialize contextMenu 
            this.contextMenu = new System.Windows.Forms.ContextMenu();
            System.Windows.Forms.MenuItem statusItem = new System.Windows.Forms.MenuItem("Status");
            statusItem.MenuItems.Add(0, new System.Windows.Forms.MenuItem("Online", new System.EventHandler(Status_Click)));
            statusItem.MenuItems.Add(1, new System.Windows.Forms.MenuItem("Offline", new System.EventHandler(Status_Click)));
            this.contextMenu.MenuItems.Add(0, statusItem);
            this.contextMenu.MenuItems.Add(1, new System.Windows.Forms.MenuItem("Show", new System.EventHandler(Show_Click)));
            this.contextMenu.MenuItems.Add(2, new System.Windows.Forms.MenuItem("Exit", new System.EventHandler(Exit_Click)));
           
            
            //initialize balloon items
            string title_ball = "PDS_Condividi";
            //NON SO COME PASSARGLI IL FILE
            string text_ball = _TCPListener.ServeClientA("").ToString();

            //Initialize icon
            if (_referenceData.LocalUser.Status.Equals("online")){
                ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_green.ico"));
            } else {
                ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_red.ico"));
            }
            
            ni.Visible = true;
            //attacco il context menù all'icona
            ni.ContextMenu = this.contextMenu;
            //attacco un balloon all'icona
            ni.ShowBalloonTip(5, title_ball, text_ball, ToolTipIcon.Info);
            ni.Text = "PDS_Condividi";
            ni.DoubleClick +=
                delegate (object sender, EventArgs args){
                    this.Show();
                    this.WindowState = WindowState.Normal;
                };

            // Inizializzo info user
            textUserName.Text = _referenceData.LocalUser.Name;
            if (_referenceData.LocalUser.Status.Equals("online")) {
                comboStatus.Text = "Online";
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("green_dot.png")));
            } else {
                comboStatus.Text = "Offline";
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("red_dot.png")));
            }

            // Caricamento immagine profilo, cambio comportamento a seconda immagine di default o no
            // TODO: vedere se fare una copia o no e lasciarla interna al sistema
            string filename = _referenceData.LocalUser.ProfileImagePath;
            if (_referenceData.LocalUser.ProfileImagePath.Equals(_referenceData.defaultImage)) {
                filename = Utility.FileNameToHost(_referenceData.defaultImage);
            }
               ImageBrush imgBrush = new ImageBrush();
               imgBrush.ImageSource = new BitmapImage(new Uri(filename));
               ImageProfile.Fill = imgBrush;

            // Ogni secondo invia un pacchetto UDP per avvisare gli altri di possibili aggiornamenti sullo stato, nome o immagine
            // dell'utente corrente
            DispatcherTimer dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Tick += new EventHandler(dispatcherTimer_Tick);
            dispatcherTimer.Interval = new TimeSpan(0, 0, 1);
            dispatcherTimer.Start();
            if (_referenceData.useTask) {
                Task.Run(async () => { await _UDPListener.ListenerA(source.Token); });
                Task.Run(() => { PipeClient(); });
            } else {
                Thread t = new Thread(new ThreadStart(_UDPListener.Listener));
                t.Start();
                Thread tc = new Thread(new ThreadStart(PipeClient));
                tc.Start();
            }

            StartTCPListener();

            _referenceData.isFirst = true;

            // Aggiunge registri per menù contestuale
            /** TODO: AddOptionContextMenu dovrà essere spostato per essere eseguito solo dallo Wizard o durante le operazioni di 
             * configurazioni una sola volta in modalità Admin. Per adesso il codice si limita a controllare se l'esecuzione è in
             * modalità admin o no ed esegue il metodo di conseguenza */
            AddOptionContextMenu();
        }
        
        /// <summary>
        /// Gestione eventi del context menù (icona in basso)
        /// </summary>        
        protected void Status_Click(Object sender, System.EventArgs e){
            MenuItem statItem = (MenuItem) sender;
            if (statItem.Text == "Online"){
                Console.WriteLine("ONLINE");
                comboStatus.Text = "Online";
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("green_dot.png")));
                ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_green.ico"));
                _referenceData.LocalUser.Status = "online";
                _referenceData.SaveJson();
            }else{
                Console.WriteLine("OFFLINE");
                comboStatus.Text = "Offline";
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("red_dot.png")));
                ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_red.ico"));
                _referenceData.LocalUser.Status = "offline";
                _referenceData.SaveJson();
            }
        }
        protected void Show_Click(Object sender, System.EventArgs e) {
            this.Show();
            this.WindowState = WindowState.Normal;
        }
        protected void Exit_Click(Object sender, System.EventArgs e){
            Close();
        }
        ///
        

        /// <summary>
        /// Test inserimento opzione menù contestuale (tasto destro), funziona per ora ma non fa nulla
        /// da gestire la comunicazione tra processi o trovare un modo per evitare di avere 2 istanze dello stesso processo
        /// </summary>
        private void AddOptionContextMenu() {
            var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            if (principal.IsInRole(WindowsBuiltInRole.Administrator)) {
                string nameExe = Process.GetCurrentProcess().ProcessName + ".exe";
                string pathExe = Utility.FileNameToPath("", nameExe);

                // Set RegistryKey per file
                //RegistryKey _key = Registry.ClassesRoot.OpenSubKey("Directory\\Shell", true);
                RegistryKey _key = Registry.ClassesRoot.OpenSubKey("*\\Shell", true);
                RegistryKey newkey = _key.CreateSubKey("PDSApp Invia File");
                RegistryKey subNewkey = newkey.CreateSubKey("Command");

                //subNewkey.SetValue("", "D:\\Utenti\\GMoody\\Documents\\Università\\Primo Anno Magistrale\\2 - Programmazione di Sistema\\MalnatiProgetto\\PDSProjectGIT\\PDSProject\\PDSProject\\bin\\Debug\\PDSProject.exe %1");//"C:\\Program Files\\7-Zip\\7zG.exe");
                subNewkey.SetValue("", pathExe + " %1");

                // Set RegistryKey per directory
                _key = Registry.ClassesRoot.OpenSubKey("Directory\\Shell", true);
                newkey = _key.CreateSubKey("PDSApp Invia Cartella");
                subNewkey = newkey.CreateSubKey("Command");

                subNewkey.SetValue("", pathExe + " %1");

                subNewkey.Close();
                newkey.Close();
                _key.Close();
            }
        }

        /// <summary>
        /// Implementazione della NamedPipeClient
        /// Questa rimane in ascolto di possibili istanze PSDProject istanziate col menù contestuale, queste inviano il path del file da inviare all'istanza principale
        /// e termina l'esecuzione subito dopo
        /// /// </summary>
        /// <param name="e"></param>
        private void PipeClient() {
            while (true) {
                using (NamedPipeClientStream pipeClient =
                    new NamedPipeClientStream(".", "PSDPipe", PipeDirection.In)) {
                    pipeClient.Connect();

                    Console.WriteLine("Connected to pipe.");
                    Console.WriteLine("There are currently {0} pipe server instances open.",
                        pipeClient.NumberOfServerInstances);
                    using (StreamReader sr = new StreamReader(pipeClient)) {
                        // Display the read text to the console
                        string temp;
                        while ((temp = sr.ReadLine()) != null) {
                            Console.WriteLine("Received from server: {0}", temp);
                            string copia = temp;
                            _referenceData.PathFileToSend.Add(copia);
                            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { Test(copia); }));
                        }
                    }
                }
            }
        }

        public void StartTCPListener() {
            if (_referenceData.useTask) {
                CancellationToken token = source.Token;
                Task.Run(async () => { await _TCPListener.ListenerA(token); });
            } else {
                Thread t = new Thread(new ThreadStart(_TCPListener.ListenerB));
                t.Start();
            }

        }

        public void SendCallback() {
            _TCPListener.StopServer();
        }

        public void Test(string e) {
            textInfoMessage.Text += e + "\n";
        }

        private void dispatcherTimer_Tick(object sender, EventArgs e) {
            // Controllo se c'è stato un cambio di rete 
            if (_referenceData.LocalIPAddress.Equals("") && _referenceData.BroadcastIPAddress.Equals("")){
                foreach (KeyValuePair<string, string> t in _referenceData.Ips){
                    _UDPSender.Sender(t.Key);
                }
            }else{
                _UDPSender.Sender(_referenceData.BroadcastIPAddress);
                long currentTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                lock (_referenceData.Users) {
                    foreach(KeyValuePair<string,Host> u in _referenceData.Users) {
                        if((currentTime - u.Value.LastPacketTime) > 10000 && u.Value.Status.Equals("online")){
                            u.Value.UpdateStatus("offline");
                            UpdateProfileHost(u.Key);
                        }
                    }
                }
                if (Directory.GetFiles(Utility.PathTmp()).Count() > 1) {
                    foreach (string file in Directory.GetFiles(Utility.PathTmp())) {
                        if (!file.Contains("README")) {
                            int users = _referenceData.FileToFinish.Where(c => c.Value.ContainsKey(file)).Count();
                            if (_referenceData.FileToFinish.Where(c => c.Value.ContainsKey(file) && (c.Value)[file].Equals("end")).Count() >= users){
                                Console.WriteLine("Cancellazione file..." + file);
                                try
                                {
                                    File.Delete(file);
                                } catch(IOException exp)
                                {
                                    Console.WriteLine($"Exception : {exp.Message}");
                                }
                            }
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Gestione stato Minimize (icona nella tray)
        /// </summary>
        protected override void OnStateChanged(EventArgs e) {
            if (WindowState == System.Windows.WindowState.Minimized)
                this.Hide();

            base.OnStateChanged(e);
        }

        /// <summary>
        /// Gestione evento OnClosing (messageBox)
        /// </summary>
        private void MainWindow_Closing(object sender, CancelEventArgs e) {

            // Configure the message box to be displayed
            string messageBoxText = "Vuoi chiudere l'applicazione?\n(Cliccando ''NO'' rimarrà attiva in basso)";
            string caption = "Attenzione";
            MessageBoxButton button = MessageBoxButton.YesNo;
            MessageBoxImage icon = MessageBoxImage.Question;

            // Display message box
            MessageBoxResult result = MessageBox.Show(messageBoxText, caption, button, icon);

            switch (result) {
                case MessageBoxResult.Yes:
                    e.Cancel = false;
                    if (_referenceData.useTask) {
                        source.Cancel();
                    }
                    _TCPListener.StopServer();
                    break;
                case MessageBoxResult.No:
                    this.WindowState = WindowState.Minimized;
                    e.Cancel = true;
                    break;
            }

        }

        ///// <summary>
        ///// Selezionando l'icona dell'amico, appare/scompare un canvas blu
        ///// </summary>
        //private void Canvas_Visible(object sender, MouseButtonEventArgs e) {

        //    if (selected == false) {
        //        canvasSelect.Background = new SolidColorBrush(Colors.SkyBlue);
        //        SendButton.Visibility = Visibility.Visible;
        //        UndoButton.Visibility = Visibility.Visible;
        //        selected = true;
        //        if (_referenceData.Users.Count > 0) {
        //            _referenceData.selectedHost = _referenceData.Users.First().Key;
        //        }
        //    } else {
        //        canvasSelect.Background = new SolidColorBrush(Colors.AliceBlue);
        //        SendButton.Visibility = Visibility.Hidden;
        //        UndoButton.Visibility = Visibility.Hidden;
        //        selected = false;
        //        _referenceData.selectedHost = "";
        //    }
        //}

        /// <summary>
        /// Modifica immagine profilo
        /// </summary>
        private void Image_MouseDown(object sender, MouseButtonEventArgs e) {
            // Strumento base di windows per scegliere un file nel filesystem
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();

            // Formato file di default e filtri sui file che si possono selezionare. In questo caso solo i file immagine.
            dlg.DefaultExt = ".png";
            dlg.Filter =
                "Image files(*.jpg, *.jpeg, *.jpe, *.jfif, *.png) | *.jpg; *.jpeg; *.jpe; *.jfif; *.png"; //"JPEG Files (*.jpeg)|*.jpeg|PNG Files (*.png)|*.png|JPG Files (*.jpg)|*.jpg|GIF Files (*.gif)|*.gif";

            // Col metodo ShowDialog viene visualizzata la schermata di OpenFileDialog
            Nullable<bool> result = dlg.ShowDialog();

            //Se l'utente seleziona effettivamente qualcosa...
            if (result == true) {
                // Apro il file e lo sostituisco all'immagine corrente SOLO se è diverso, in questo caso uso l'hash
                // TODO: da rivedere uso hash + dimensioni
                string filename = dlg.FileName;
                //ImageProfile.Width = 50; ImageProfile.Height = 50;
                using (SHA256 sha = SHA256.Create()) {
                    FileStream file = File.OpenRead(filename);
                    byte[] hash = sha.ComputeHash(file);
                    if (!BitConverter.ToString(hash).Replace("-", String.Empty)
                        .Equals(_referenceData.LocalUser.ProfileImageHash)) {
                        ImageBrush imgBrush = new ImageBrush();
                        imgBrush.ImageSource = new BitmapImage(new Uri(filename));

                        ImageProfile.Fill = imgBrush;
                        ImageSettingsProfile.Fill = imgBrush;

                        string hashImage = BitConverter.ToString(hash).Replace("-", String.Empty);

                        if (!hashImage.Equals(_referenceData.LocalUser.ProfileImageHash)) {
                            _referenceData.LocalUser.ProfileImageHash = hashImage;
                            _referenceData.LocalUser.ProfileImagePath = filename;
                            _referenceData.SaveJson();
                            _referenceData.hasChangedProfileImage = true;
                            //TODO: invio a tutti gli host in rete
                            //if (_referenceData.useTask)
                            //{
                            //    _referenceData.FileToFinish.AddOrUpdate(_referenceData.LocalUser.ProfileImagePath, (key) => "start", (key,value) =>"inprogress");
                            //    await _TCPSender.SendA(new List<string>() { _referenceData.LocalUser.ProfileImagePath }, true); // Deve essere inviato a tutti gli utenti connessi 
                            //}
                            //else
                            //{
                            //    Thread t = new Thread(new ParameterizedThreadStart(_TCPSender.Send));
                            //    t.Start(new List<string>() { _referenceData.LocalUser.ProfileImagePath });
                            //}
                        }

                    }
                }
            }
        }

        /// <summary>
        /// Setting Visible
        /// </summary>
        private void Settings_visible(object sender, MouseButtonEventArgs e) {
            SettingsCanvas.Visibility = Visibility.Visible;
            MainCanvas.Visibility = Visibility.Hidden;

            //carico l'immagine di profilo
            string filename = _referenceData.LocalUser.ProfileImagePath;
            if (_referenceData.LocalUser.ProfileImagePath.Equals(_referenceData.defaultImage))
                filename = Utility.FileNameToHost(_referenceData.defaultImage);
            ImageBrush imgBrush = new ImageBrush();
            imgBrush.ImageSource = new BitmapImage(new Uri(filename));

            ImageSettingsProfile.Fill = imgBrush;

            //carico l'ultimo username che avevo
            textChangeName.Text = _referenceData.LocalUser.Name;
        }

        /// <summary>
        /// Main Visible
        /// </summary>
        private void BackButton_OnClick(object sender, RoutedEventArgs e) {
            SettingsCanvas.Visibility = Visibility.Hidden;
            MainCanvas.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Applicazione modifiche utente
        /// Gestione caso username vuoto
        /// </summary>
        private async void ApplyButton_OnClick(object sender, RoutedEventArgs e) {

            if (this.textChangeName.Text == ""){
                string messageWarning = "Inserire Username";
                string caption_warning = "Attenzione";
                MessageBoxImage icon_warning = MessageBoxImage.Information;
                MessageBoxButton button_warning = MessageBoxButton.OK;

                MessageBox.Show(messageWarning, caption_warning, button_warning, icon_warning);
            }else{

                // Configure the message box to be displayed
                string messageBoxText = "Modifiche Applicate";
                string caption = "Attenzione";
                MessageBoxImage icon = MessageBoxImage.Information;
                MessageBoxButton button = MessageBoxButton.OK;

                MessageBox.Show(messageBoxText, caption, button, icon);

                this.textUserName.Text = this.textChangeName.Text;

                _referenceData.LocalUser.Name = this.textChangeName.Text;
                _referenceData.LocalUser.SavePath = this.pathName.Text;

                _referenceData.SaveJson();
                if (_referenceData.useTask)
                {
                    //_referenceData.FileToFinish.AddOrUpdate(_referenceData.iè, ( key ) => "start", ( key, value ) => "inprogress");
                    await _TCPSender.SendA(new List<string>() { _referenceData.LocalUser.ProfileImagePath }, true); // Deve essere inviato a tutti gli utenti connessi 
                }
                else
                {
                    Thread t = new Thread(new ParameterizedThreadStart(_TCPSender.Send));
                    t.Start(new List<string>() { _referenceData.LocalUser.ProfileImagePath });
                }
            }

        }

        /// <summary>
        /// Modifica path salvataggio
        /// </summary>
        private void CheckBox_Uncheck(object sender, RoutedEventArgs e) {
            folderButton.Visibility = Visibility.Visible;
            pathName.IsReadOnly = false;
            pathName.Background = new SolidColorBrush(Colors.White);
        }

        /// <summary>
        /// Path Predefinito
        /// </summary>
        private void CheckBox_Check(object sender, RoutedEventArgs e) {
            folderButton.Visibility = Visibility.Hidden;
            pathName.IsReadOnly = true;
            pathName.Background = new SolidColorBrush(Colors.LightGray);
            pathName.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        /// <summary>
        /// Selezione path personalizzato
        /// </summary>
        private void FolderButton_OnClick(object sender, RoutedEventArgs e) {

            var save_dlg = new CommonOpenFileDialog();
            save_dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            save_dlg.IsFolderPicker = true;
            CommonFileDialogResult result = save_dlg.ShowDialog();
            if (result.ToString() == "Ok") {
                save_path = save_dlg.FileName;
                pathName.Text = save_path;
            }
        }

        /// <summary>
        /// Combobox Status changed (non sicura funzionamento)
        /// </summary>
        private void ComboStatus_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (comboStatus.Text == "Online") {
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("red_dot.png")));
                ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_red.ico"));
                _referenceData.LocalUser.Status = "offline";
                _referenceData.SaveJson();
            } else if (comboStatus.Text == "Offline") {
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("green_dot.png")));
                ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_green.ico"));
                _referenceData.LocalUser.Status = "online";
                _referenceData.SaveJson();
            }
        }

        /// <summary>
        /// Modifica info amico
        /// </summary>
        public void UpdateProfileHost(string ip) {
            //MainWindow.main.textNFriend.Text = _referenceData.Users[ip].Name;
            //MainWindow.main.textSFriend.Text = _referenceData.Users[ip].Status;

            //if (_referenceData.Users[ip].Status == "online") {
            //    MainWindow.main.textSFriend.Foreground = new SolidColorBrush(Colors.Green);
            //    friendStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("green_dot.png")));
            //} else if (_referenceData.Users[ip].Status == "offline" || _referenceData.Users[ip].Status == "") {
            //    MainWindow.main.textSFriend.Foreground = new SolidColorBrush(Colors.DarkRed);
            //    friendStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("red_dot.png")));
            //}

            //HostImage.Width = 50; HostImage.Height = 50;
            string filename = "";
            string tmp_name = "";

            if (!(tmp_name = _referenceData.Users[ip].ProfileImagePath).Equals("") && File.Exists(tmp_name))
                filename = _referenceData.Users[ip].ProfileImagePath;
            else {
                if (_referenceData.Users[ip].ProfileImagePath.Equals(_referenceData.defaultImage) ||
                    !_referenceData.UserImageChange.ContainsKey(_referenceData.Users[ip].ProfileImageHash))
                    filename = Utility.FileNameToHost(_referenceData.defaultImage);
                else //if (_referenceData.UserImageChange.ContainsKey(_referenceData.Users[ip].ProfileImageHash))
                {
                    filename = _referenceData.Users[ip].ProfileImagePath;

                    /*string currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                    string[] files = Directory.GetFiles(currentDirectory, _referenceData.Users[ip].ProfileImagePath);
                    filename = files[0];*/
                }
            }

            if (filename.Equals("")) return;
                try {
                    var file = File.OpenRead(filename);
                    //ImageBrush imgBrush = new ImageBrush();
                    //imgBrush.ImageSource = new BitmapImage(new Uri(filename));

                    //imageFriend.Fill = imgBrush;
                    file.Close();
                    List<Host> lista = new List<Host>();
                    foreach (var item in friendList.SelectedItems) {
                        lista.Add((Host)item);
                    }
                    friendList.Items.Refresh();
                    friendList.SelectedIndex = -1;
                    foreach (var item in friendList.Items) {
                        if (lista.Contains(item) && ((Host)item).Status.Equals("online")) {
                            friendList.SelectedItems.Add(item);
                        }
                        if(((Host)item).Ip.Equals(ip) && ((Host)item).Status.Equals("online"))
                            friendList.SelectedItems.Add(item);
                        
                    }

                } catch (UnauthorizedAccessException e) {
                    Console.WriteLine($"File not yet reciced : {e.Message}");
                } catch (Exception e) {
                    Console.WriteLine($"Exception : {e.Message}");
                }

                //HostImage.Source = new BitmapImage(new Uri(filename));
            }
        
        /// <summary>
        /// Aggiorna immagine del profilo (invia l'immagine)
        /// </summary>
        public async void SendProfileImage() {
            _referenceData.hasChangedProfileImage = true;
            //if(_referenceData.FileToFinish.ContainsKey(_referenceData.LocalUser.ProfileImagePath))
            //    _referenceData.FileToFinish.GetOrAdd(_referenceData.LocalUser.ProfileImagePath, "start");
            if (_referenceData.useTask)
            {
                await Task.Run(async() => {
                    await _TCPSender.SendA(new List<string>() {_referenceData.LocalUser.ProfileImagePath}, true);
                }); 
            }
            else
            {

                Thread t = new Thread(new ParameterizedThreadStart(_TCPSender.Send));
                t.Start(new List<string>() { _referenceData.LocalUser.ProfileImagePath });
            }
        }

        /// <summary>
        /// Invio file
        /// </summary>
        private async void ButtonSend_Click(object sender, RoutedEventArgs e) {

            if (_referenceData.PathFileToSend.Count > 0 && _referenceData.selectedHosts.Count > 0) {
                //Task.Run(() => { _TCPSender.Send(_referenceData.PathFileToSend); });
                //foreach (string path in _referenceData.PathFileToSend)
                //_referenceData.FileToFinish.AddOrUpdate(path, ( key ) => "start", ( key, value ) => "inprogress");
                
                
                List<string> selectedCurrntlyHost;
                lock (_referenceData.selectedHosts){
                    selectedCurrntlyHost = _referenceData.selectedHosts.ToList();
                }

                // Test zip Da vedere meglio
                /*string zipPath = @Utility.PathTmp() + "\\" + DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString() + ".zip";
                using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create)) {
                    foreach (string path in  _referenceData.PathFileToSend)
                        archive.CreateEntryFromFile(path, Utility.PathToFileName(path));
                }*/
                List<string> pathFiles;
                lock (_referenceData.PathFileToSend)
                {
                    string zipPath = @Utility.PathTmp() + "\\" + DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString() + ".zip";
                    using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                    {
                        
                        foreach (string path in _referenceData.PathFileToSend)
                            archive.CreateEntryFromFile(path, Utility.PathToFileName(path));
                    }

                    foreach (string ip in selectedCurrntlyHost)
                    {
                        Dictionary<string, string> listFile = new Dictionary<string, string>();
                        listFile.Add(zipPath, "start");
                        _referenceData.FileToFinish.AddOrUpdate(ip, (key) => listFile, (key, value) => {
                            return value.Concat(listFile).ToDictionary(x => x.Key, x => x.Value);
                        });
                    }
                    pathFiles = new List<string>();// _referenceData.PathFileToSend.ToList();
                    pathFiles.Add(zipPath);
                    _referenceData.PathFileToSend.Clear();

                }
                /*
                foreach (string ip in selectedCurrntlyHost) {
                    Dictionary<string, string> listFile = _referenceData.PathFileToSend.ToDictionary( key => key, value => "start");
                    _referenceData.FileToFinish.AddOrUpdate(ip, ( key ) => listFile, ( key, value ) => {
                        var difference = listFile.Where(entry => !value.ContainsKey(entry.Key)).ToDictionary(x => x.Key, x => x.Value);
                        return value.Concat(difference).ToDictionary(x => x.Key, x => x.Value);
                        });
                }
                List<string> pathFiles = _referenceData.PathFileToSend.ToList();
                _referenceData.PathFileToSend.Clear();
                */
                if (_referenceData.useTask) {
                    await _TCPSender.SendA(pathFiles, false);
                    //NOME FILE SOPRA PROGRESS BAR)
                    string file_path = _referenceData.FileToFinish.Keys.ToString();
                    string file_name = Utility.PathToFileName(file_path);
                    textFile.Text = file_name;
                    //
                } else {
                    Thread t = new Thread(new ParameterizedThreadStart(_TCPSender.Send));
                    t.Start(_referenceData.FileToFinish.Keys.ToList());
                }
            }
        }

        private void friendList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
            Console.WriteLine(e.AddedItems);
            if (e.AddedItems.Count > 0){
                //_referenceData.selectedHost = _referenceData.Users.First().Key;
                foreach (Host h in e.AddedItems) {
                    if (!_referenceData.selectedHosts.Contains(h.Ip))
                        _referenceData.selectedHosts.Add(h.Ip);
                    Console.WriteLine("UTENTE SELEZIONATO " + h.Ip);
                    if (h.Status.Equals("offline"))
                        friendList.SelectedItems.Remove(h);
                }
                //Console.WriteLine("UTENTE SELEZIONATO");
            }
            if (e.RemovedItems.Count > 0) {
                foreach (Host h in e.RemovedItems)
                {
                    _referenceData.selectedHosts.Remove(h.Ip);
                    Console.WriteLine("UTENTE DESELEZIONATO " + h.Ip);
                }
            }
            /*else {
             _referenceData.selectedHost = "";
            Console.WriteLine("UTENTE DESELEZIONATO");
        }*/
        }

        private void FriendList_DoubleClick(object sender, MouseButtonEventArgs e) {
            ListBox list = (ListBox)sender;
            if (list.SelectedItems.Count == 1) {
                friendList.SelectedIndex = -1;
            }
        }
    }
}