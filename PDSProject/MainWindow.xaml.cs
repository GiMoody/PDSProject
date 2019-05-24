using System;

using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;

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

using System.Threading;
using System.Threading.Tasks;

using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;

using MessageBox = System.Windows.MessageBox;
using ListBox = System.Windows.Controls.ListBox;
using MenuItem = System.Windows.Forms.MenuItem;
using Path = System.IO.Path;
using System.Collections.ObjectModel;
using Button = System.Windows.Controls.Button;
using Application = System.Windows.Application;
using TextBox = System.Windows.Controls.TextBox;
using System.Windows.Media.Animation;
using System.Drawing;
using System.Windows.Controls.Primitives;

namespace PDSProject {
    /// <summary>
    /// Logica di interazione per MainWindow.xaml
    /// </summary>

    public partial class MainWindow : Window {

        private string save_path;
        public static MainWindow main; // Usato come riferimento da parte delle altri classi
        public ObservableCollection<FileRecive> fileReciveList = new ObservableCollection<FileRecive>();

        SharedInfo _referenceData = SharedInfo.Instance;
        MyTCPListener _TCPListener;
        MyTCPSender _TCPSender;
        MyUDPListener _UDPListener;
        MyUDPSender _UDPSender;
        CancellationTokenSource source;

        SemaphoreSlim obj = new SemaphoreSlim(0);

        System.Windows.Forms.ContextMenu contextMenu;
        NotifyIcon ni = new NotifyIcon();

        DispatcherTimer flashTimer = new DispatcherTimer();
        private Icon[] icons;
        private int currentIcon;

        
        public MainWindow() {
            InitializeComponent();
            
            main = this;
            _TCPListener = new MyTCPListener();
            _TCPSender = new MyTCPSender();
            _UDPListener = new MyUDPListener();
            _UDPSender = new MyUDPSender();

            source = new CancellationTokenSource();
      
            //Initialize friendList data
            friendList.ItemsSource = _referenceData.Users.Values;
           
            // Initialize contextMenu 
            this.contextMenu = new System.Windows.Forms.ContextMenu();
            System.Windows.Forms.MenuItem statusItem = new System.Windows.Forms.MenuItem("Status");
            statusItem.MenuItems.Add(0, new System.Windows.Forms.MenuItem("Online", new System.EventHandler(Status_Click)));
            statusItem.MenuItems.Add(1, new System.Windows.Forms.MenuItem("Offline", new System.EventHandler(Status_Click)));
            this.contextMenu.MenuItems.Add(0, statusItem);
            this.contextMenu.MenuItems.Add(1, new System.Windows.Forms.MenuItem("Show", new System.EventHandler(Show_Click)));
            this.contextMenu.MenuItems.Add(2, new System.Windows.Forms.MenuItem("Exit", new System.EventHandler(Exit_Click)));

            // Inizializzo i dati dell'utente locale
            InitLocalUserData();
            DataContext = fileReciveList;
            fileList.ItemContainerGenerator.StatusChanged += ItemContainerGeneratorStatusChanged;

            // Attacco il context menù all'icona
            ni.Visible = true;
            ni.ContextMenu = this.contextMenu;
            ni.Text = "PDS_Condividi";
            ni.BalloonTipClicked += new EventHandler(notifyIcon_BalloonTipClicked);
            ni.DoubleClick +=
                delegate ( object sender, EventArgs args ) {
                    this.Show();
                    this.WindowState = WindowState.Normal;
                };

            // Ogni secondo invia un pacchetto UDP per avvisare gli altri di possibili aggiornamenti sullo stato, nome o immagine
            // dell'utente corrente
            DispatcherTimer dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Tick += new EventHandler(dispatcherTimer_Tick);
            dispatcherTimer.Interval = new TimeSpan(0, 0, 1);
            dispatcherTimer.Start();
                        
            Task.Run(async () => {
                try {
                    await _UDPListener.Listener(source.Token);
                }catch(Exception e) {
                    Console.WriteLine($"On _UDPListener Task. Exception {e}");
                }
            });
            Task.Run(() => { PipeClient(); });
            
            // Setta bool per avvisare che è il primo avvio del listener
            _referenceData.isFirst = true;
            StartTCPListener();

            //initialize timer for flashing icon           
            icons = new Icon[2];
            icons[0] = new System.Drawing.Icon(Utility.FileNameToSystem("share_green.ico"));
            icons[1] = new System.Drawing.Icon(Utility.FileNameToSystem("share_black.ico"));
            flashTimer.Tick += new EventHandler(IconBlinking);
            flashTimer.Interval = new TimeSpan(0, 0, 1);

            // Aggiunge registri per menù contestuale
            /** TODO: AddOptionContextMenu dovrà essere spostato per essere eseguito solo dallo Wizard o durante le operazioni di 
             * configurazioni una sola volta in modalità Admin. Per adesso il codice si limita a controllare se l'esecuzione è in
             * modalità admin o no ed esegue il metodo di conseguenza */
            AddOptionContextMenu();

        }

        /// <summary>
        /// Gestisce il blink dell'icona nel caso di ricezione file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void IconBlinking (object sender, EventArgs e){
            ni.Icon = icons[currentIcon];
            if (currentIcon++ == 1){
                currentIcon = 0;
            }
        }

        public void FlashingWindowIcon() {
            main.FlashWindow();
        }

        private void ItemContainerGeneratorStatusChanged(object sender, EventArgs e) {
            if(fileList.ItemContainerGenerator.Status
                == GeneratorStatus.ContainersGenerated) {
                var containers = fileList.Items.Cast<object>().Select(
                    item => (FrameworkElement)fileList
                        .ItemContainerGenerator.ContainerFromItem(item));

                foreach(var container in containers) {
                    if(container != null) {
                        container.Loaded += ItemContainerLoaded;
                    }

                }
            }
        }

        private void ItemContainerLoaded(object sender, RoutedEventArgs e) {
            var element = (FrameworkElement)sender;
            element.Loaded -= ItemContainerLoaded;
            
            ListBoxItem fr = sender as ListBoxItem;
            FileRecive fr_listbox = fr.DataContext as FileRecive;

            string title_ball = "PDS_Condividi";
            string text_ball = "Utente " + fr_listbox.hostName + " ti vuole inviare un file!";
            int index = (int)fileList.Items.IndexOf(GetFileReciveByFileName(fr_listbox.fileName));
            var currentSelectedListBoxItem = this.fileList.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem;

            if(currentSelectedListBoxItem == null) {
                fileList.UpdateLayout();
                fileList.ScrollIntoView(fileList.Items[index]);
                currentSelectedListBoxItem = this.fileList.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem;
            }
           

            Button yesButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "yesButton");
            Button noButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "noButton");
            Button stopButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "stopButton");

            if(_referenceData.GetInfoLocalUser().AcceptAllFile) {
                yesButton.Visibility = Visibility.Hidden;
                noButton.Visibility = Visibility.Hidden;
                stopButton.Visibility = Visibility.Visible;
            } else {

                yesButton.Visibility = Visibility.Visible;
                noButton.Visibility = Visibility.Visible;
                stopButton.Visibility = Visibility.Hidden;
            }

            ni.ShowBalloonTip(5, title_ball, text_ball, ToolTipIcon.Info);
            ni.Tag = GetFileReciveByFileName(fr_listbox.fileName);
        }

        /// <summary>
        /// Metodo chiamato in fase di inizializzazione per inserire in grafica i dati dell'utente locale
        /// </summary>
        protected void InitLocalUserData() {
            // Display local user information
            //---------------------------------
            CurrentHostProfile currentLocalUser = _referenceData.GetInfoLocalUser();

            //Initialize icon
            ni.Icon = currentLocalUser.Status.Equals("online") == true ?
                      new System.Drawing.Icon(Utility.FileNameToSystem("share_green.ico")) :
                      new System.Drawing.Icon(Utility.FileNameToSystem("share_red.ico"));
            
            // Inizializzo info user grafica
            textUserName.Text = currentLocalUser.Name;

            // Update status
            if (currentLocalUser.Status.Equals("online")) {
                comboStatus.Text        = "Online";
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("green_dot.png")));
            }
            else {
                comboStatus.Text        = "Offline";
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("red_dot.png")));
            }
            comboStatus.SelectedIndex = currentLocalUser.Status.ToString() == "online" ? 0 : 1;

            // Update SavePath & AcceptFileConfiguration
            if (currentLocalUser.SavePath.Equals(_referenceData.defaultPath)) {
                ChoosePath.IsChecked = true;
                pathName.Text = _referenceData.defaultPath;
            }
            else {
                ChoosePath.IsChecked = false;
                pathName.Text = currentLocalUser.SavePath;
            }
            AcceptFile.IsChecked = currentLocalUser.AcceptAllFile == true ? true : false;

            // Caricamento immagine profilo, cambio comportamento a seconda immagine di default o no
            string filename = currentLocalUser.ProfileImagePath;

            if (currentLocalUser.ProfileImagePath.Equals(_referenceData.defaultImage))
                filename = Utility.FileNameToHost(_referenceData.defaultImage);

            ImageBrush imgBrush = new ImageBrush();
            imgBrush.ImageSource = new BitmapImage(new Uri(filename));
            ImageProfile.Fill = imgBrush;

            //Inizializzo FILE LIST (SOLO PER TESTING)
            //ObservableCollection<FileRecive> items = new ObservableCollection<FileRecive>();
            //items.Add(new FileRecive() { hostName = "Giulia", fileName = "Prova.zip", statusFile = "Ricezione", estimatedTime = "00:02", dataRecived = 50 });
            //items.Add(new FileRecive() { hostName = "Rossella", fileName = "Prova_2.zip", statusFile = "Attesa Conferma", estimatedTime = "00:15", dataRecived = 30 });

            //fileList.ItemsSource = items;
        }

        /// <summary>
        /// Metodo che permette il flash delle icone all'avvio del popup
        /// </summary>
        public void NotifySistem() {
            //Make icon taskbar flash
            main.FlashWindow();
            //Start timer for icon flashing in tray           
            flashTimer.Start();
        }

        /// <summary>
        /// Aggiornamento oggetto fileReciveList per lista file in ricezione
        /// </summary>
        /// <param name="ipUser"></param>
        /// <param name="pathFile"></param>
        /// <param name="status"></param>
        /// <param name="estimatedTime"></param>
        /// <param name="byteReceived"></param>
        public void AddOrUpdateListFile(string ipUser, string pathFile, FileRecvStatus? status, string estimatedTime, double? byteReceived){
            if(fileReciveList.Where(e => e.fileName.Equals(pathFile)).Count() > 0) {
                //FileRecive files = fileReciveList.Where(e => e.fileName.Equals(pathFile)).ToList()[0];
                for (int i = 0; i < fileReciveList.Count; i++){
                    if (fileReciveList[i].fileName.Equals(pathFile)) {
                        if (status != null) {
                            fileReciveList[i].statusFile = status.ToString();
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
                string currentUsername = _referenceData.GetRemoteUserName(ipUser);
                FileRecive files = new FileRecive(currentUsername, pathFile, status.ToString(), "0", 0);
                fileReciveList.Add(files);              
            }

            //fileList.Items.Refresh();

        }

        /// <summary>
        /// Gestione Click Ballon di notifica ricezione file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void notifyIcon_BalloonTipClicked(object sender, EventArgs e) {
            //Porto in primo piano l'applicazione
            this.Show();
            this.WindowState = WindowState.Normal;

            main.StopFlashingWindow();

            //Evidenzio nella lista il file di cui voglio la conferma
            FileRecive fileTAG = (FileRecive)((NotifyIcon)sender).Tag;
            fileList.SelectedItems.Add(fileTAG);

            //Rendo visibili i bottoni si/no di quell'elemento
            var currentSelectedListBoxItem = this.fileList.ItemContainerGenerator.ContainerFromIndex((int)fileList.Items.IndexOf(fileTAG)) as ListBoxItem;
            

            Button yesButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "yesButton");
            Button noButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "noButton");
            Button stopButton = MainWindow.FindChild<Button>(currentSelectedListBoxItem, "stopButton");

            yesButton.Visibility = Visibility.Visible;
            noButton.Visibility = Visibility.Visible;
            stopButton.Visibility = Visibility.Hidden;

        }
              
        /// <summary>
        /// Gestione eventi del context menù (icona in basso)
        /// </summary>        
        protected void Status_Click(Object sender, System.EventArgs e){
            MenuItem statItem = (MenuItem) sender;
            if (statItem.Text == "Online") {
                Console.WriteLine("ONLINE");
                comboStatus.Text = "Online";
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("green_dot.png")));
                ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_green.ico"));
                _referenceData.UpdateStatusLocalUser("online");
            }else{
                Console.WriteLine("OFFLINE");
                comboStatus.Text = "Offline";
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("red_dot.png")));
                ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_red.ico"));
                _referenceData.UpdateStatusLocalUser("offline");
            }
        }
        protected void Show_Click(Object sender, System.EventArgs e) {
            this.Show();
            this.WindowState = WindowState.Normal;
        }
        protected void Exit_Click(Object sender, System.EventArgs e){
            Close();
        }
        
        /// <summary>
        /// Gestione stato Minimize (icona nella tray)
        /// </summary>
        protected override void OnStateChanged(EventArgs e) {
            if(WindowState == System.Windows.WindowState.Minimized)
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

            switch(result) {
                case MessageBoxResult.Yes:
                e.Cancel = false;
                source.Cancel();
                _TCPListener.StopServer();
                break;
                case MessageBoxResult.No:
                this.WindowState = WindowState.Minimized;
                e.Cancel = true;
                break;
            }
        }

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
                        string path;
                        while ((path = sr.ReadLine()) != null) {
                            Console.WriteLine("Received from server: {0}", path);
                            _referenceData.AddPathToSend(path);
                            string copia = path;

                            // Todo: da eliminare
                            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { Test(copia); }));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Metodo che fa partire il TCPListener corrente
        /// </summary>
        public void StartTCPListener() {
            CancellationToken token = source.Token;
            Task.Run(async () => {
                try {
                    await _TCPListener.Listener(token);
                }
                catch (Exception e) {
                    Console.WriteLine($"Exception on StartTCPListener Task - {e}");
                }
            });
        }

        /// <summary>
        /// Metodo che fa termina il TCPListener corrente
        /// </summary>
        public void StopTCPListener() {
            _TCPListener.StopServer();
        }

        /// <summary>
        /// TODO: Da eliminare prima o poi
        /// </summary>
        /// <param name="e"></param>
        public void Test(string e) {
            textInfoMessage.Text += e + "\n";
            if(_referenceData.GetPathFileToSend().Count > 0) {
                UndoButton.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// TODO: Da eliminare prima o poi
        /// </summary>
        /// <param name="e"></param>
        public async void TestResponse(List<string> filenames, string ip) {
            Random rand = new Random();
            foreach (string file in filenames) {
                try
                {
                    if (true)//rand.Next() % 2 == 0)
                    {
                        // Si
                        Console.WriteLine("Confermo file " + file + " dall'utente " + ip);
                        await _TCPSender.SendResponse(ip, file, PacketType.YFILE);
                    }
                    else
                    {
                        Console.WriteLine("Rifiuto file " + file + " dall'utente " + ip);
                        await _TCPSender.SendResponse(ip, file, PacketType.NFILE);
                    }
                }catch(Exception e)
                {
                    Console.WriteLine($"Exception on TestResponse Task - {e}");
                }
            }
        }

        /// <summary>
        /// TODO: Da eliminare prima o poi
        /// </summary>
        /// <param name="e"></param>
        public async void SendResponse (string filename, string ip, PacketType type ) {
            try {
                FileRecvStatus status = type == PacketType.YFILE ? FileRecvStatus.YSEND : FileRecvStatus.NSEND;
                AddOrUpdateListFile(ip, filename, status, "-", 0.0f);
                await _TCPSender.SendResponse(ip, filename, type);
            }
            catch (Exception e) {
                Console.WriteLine($"Exception on TestResponse Task - {e}");
            }
        }        
       
        /// <summary>
        /// Metodo chiamato in caso di conferma di ricezione di un file
        /// </summary>
        /// <param name="filename">Nome del file</param>
        /// <param name="ip">Ip del mittente </param>
        public async void SendFile(string filename, string ip) {
            try {
                await _TCPSender.SendFile(Utility.PathToFileName(filename), ip);
            }
            catch(Exception e) {
                Console.WriteLine($"Exception on SendFile Task -{e}");
            }
        }

        public FileRecive GetFileReciveByFileName (string fileName) {
            if (fileReciveList.Where(e => e.fileName.Equals(fileName)).Count() > 0) {
                return fileReciveList.Where(e => e.fileName.Equals(fileName)).ToList()[0];
            }
            else
                return null;
        }

        /// <summary>
        /// Metodo chiamato dal DispatcherTimer per inviare ogni secondo un pacchetto UDP con le informazioni
        /// dell'host corrente agli altri utenti della rete.
        /// In più esegue alcune operazioni di pulizia come file temporanei e controllo degli utenti disconnessi
        /// </summary>
        private void dispatcherTimer_Tick(object sender, EventArgs e) {
            if (!_referenceData.CheckIfConfigurationIsSet()){
                // In caso di rete non configurata invio pacchetti UDP ad ogni sottorete associata alle 
                // interfaccie di rete del sistema
                foreach (string ip in _referenceData.GetListIps()){
                    _UDPSender.Sender(ip);
                }
                friendList.Items.Refresh();
            }
            else{
                // In caso la rete sia configurata la invio solo agli utenti connessi
                _UDPSender.Sender(_referenceData.GetBroadcastIPAddress());

                // Controlla se ci sono utenti disconnessi (utenti di cui non si ha notizia da almeno 10 secondi)
                long currentTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                foreach(Host u in _referenceData.GetOnlineUsers()) {
                    if((currentTime - u.LastPacketTime) > 10000) {
                        _referenceData.UpdateStatusUser(u.Ip, "offline");
                        UpdateProfileHost(u.Ip);
                    }
                }
                
                // Pulisce eventuali file temporaneai
                if (Directory.GetFiles(Utility.PathTmp()).Count() > 1) {
                    foreach (string file in Directory.GetFiles(Utility.PathTmp())) {
                        string name = Utility.PathToFileName(file);
                        if (name.Equals("README.txt")) { continue; }

                        if (_referenceData.FileSendForAllUsers(name)) {
                            Console.WriteLine("Cancellazione file..." + Utility.PathToFileName(file));
                            try {
                                File.Delete(file);
                                _referenceData.RemoveSendFile(file);
                            }
                            catch (IOException exp) {
                                Console.WriteLine($"Exception : {exp.Message}");
                            }
                        }
                        if (_referenceData.FileReceiveFromAllUsers(name)) {
                            Console.WriteLine("Cancellazione file..." + file);
                            try {
                                File.Delete(file);
                                _referenceData.RemoveRecvFile(name);
                            }
                            catch (IOException exp) {
                                Console.WriteLine($"Exception : {exp.Message}");
                            }
                        }
                    }
                }
            }

            if(this.IsActive == true) {
                main.StopFlashingWindow();
            }
        }

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
                string filename = dlg.FileName;
                using (SHA256 sha = SHA256.Create()) {
                    FileStream file = File.OpenRead(filename);
                    byte[] hash = sha.ComputeHash(file);
                    if (!BitConverter.ToString(hash).Replace("-", String.Empty)
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
        /// Setting Visible
        /// </summary>
        private void Settings_visible(object sender, MouseButtonEventArgs e) {
            SettingsCanvas.Visibility = Visibility.Visible;
            MainCanvas.Visibility = Visibility.Hidden;

            CurrentHostProfile currentLocalHost = _referenceData.GetInfoLocalUser();

            //carico l'immagine di profilo
            string filename = currentLocalHost.ProfileImagePath;
            if (currentLocalHost.ProfileImagePath.Equals(_referenceData.defaultImage))
                filename = Utility.FileNameToHost(_referenceData.defaultImage);
            ImageBrush imgBrush = new ImageBrush();
            imgBrush.ImageSource = new BitmapImage(new Uri(filename));
            
            ImageSettingsProfile.Fill = imgBrush;

            //carico l'ultimo username che avevo
            textChangeName.Text = currentLocalHost.Name;
            AcceptFile.IsChecked = currentLocalHost.AcceptAllFile;
            if (currentLocalHost.SavePath.Equals(_referenceData.defaultPath)) {
                ChoosePath.IsChecked = true;
                pathName.Text = _referenceData.defaultPath;
            }
            else {
                ChoosePath.IsChecked = false;
                pathName.Text = currentLocalHost.SavePath;
            }

        }

        /// <summary>
        /// Main Visible
        /// </summary>
        private void BackButton_OnClick(object sender, RoutedEventArgs e) {
            SettingsCanvas.Visibility = Visibility.Hidden;
            MainCanvas.Visibility = Visibility.Visible;

            CurrentHostProfile currentLocalHost = _referenceData.GetInfoLocalUser();

            // Resetta immagine di profilo
            string filename = currentLocalHost.ProfileImagePath;
            if (currentLocalHost.ProfileImagePath.Equals(_referenceData.defaultImage))
                filename = Utility.FileNameToHost(_referenceData.defaultImage);
            ImageBrush imgBrush = new ImageBrush();
            imgBrush.ImageSource = new BitmapImage(new Uri(filename));
            ImageProfile.Fill = imgBrush;

        }

        /// <summary>
        /// Applicazione modifiche utente
        /// Gestione caso username vuoto
        /// </summary>
        private async void ApplyButton_OnClick(object sender, RoutedEventArgs e) {
            if (this.textChangeName.Text == "") {
                string messageWarning = "Inserire Username";
                string caption_warning = "Attenzione";
                MessageBoxImage icon_warning = MessageBoxImage.Information;
                MessageBoxButton button_warning = MessageBoxButton.OK;

                MessageBox.Show(messageWarning, caption_warning, button_warning, icon_warning);
            }else {
                // Configure the message box to be displayed
                string messageBoxText = "Modifiche Applicate";
                string caption = "Attenzione";
                MessageBoxImage icon = MessageBoxImage.Information;
                MessageBoxButton button = MessageBoxButton.OK;

                MessageBox.Show(messageBoxText, caption, button, icon);

                try {
                    string pathProfileImage = ((BitmapImage)((ImageBrush)ImageSettingsProfile.Fill).ImageSource).UriSource.OriginalString.ToString();
                    using (SHA256 sha = SHA256.Create()) {

                        FileStream file = File.OpenRead(pathProfileImage);
                        byte[] hash = sha.ComputeHash(file);

                        if (!BitConverter.ToString(hash).Replace("-", String.Empty)
                            .Equals(_referenceData.GetInfoLocalUser().ProfileImageHash)) {
                            ImageBrush imgBrush = new ImageBrush();
                            imgBrush.ImageSource = new BitmapImage(new Uri(pathProfileImage));

                            ImageProfile.Fill = imgBrush;
                            ImageSettingsProfile.Fill = imgBrush;
                        }
                    }
                    _referenceData.UpdateInfoLocalUser(textChangeName.Text, pathProfileImage, pathName.Text, ChoosePath.IsChecked, AcceptFile.IsChecked);
                    textUserName.Text = textChangeName.Text;

                    if (_referenceData.GetOnlineUsers().Count > 0)
                        await _TCPSender.SendProfilePicture();
                }
                catch (Exception ex) {
                    Console.WriteLine($"Something goes wrong updating user information. Maybe the data are not corrected or unknown exception occured {ex}");
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
            pathName.Text = _referenceData.GetInfoLocalUser().SavePath;
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
        /// Combobox Status changed
        /// </summary>
        private void ComboStatus_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
            Console.WriteLine("COMBO_STATUS: " + e.AddedItems[0]);
            if (((ComboBoxItem)e.AddedItems[0]).Content.ToString().Equals("Online")) {
                _referenceData.UpdateStatusLocalUser("online");
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("green_dot.png")));
                ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_green.ico"));
                
            }
            else if (((ComboBoxItem)e.AddedItems[0]).Content.ToString().Equals("Offline")) {
                _referenceData.UpdateStatusLocalUser("offline");
                localStatusImage.Source = new BitmapImage(new Uri(Utility.FileNameToSystem("red_dot.png")));
                ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_red.ico"));
                            }
        }

        /// <summary>
        /// Modifica info Host identificato dal suo IP
        /// </summary>
        public void UpdateProfileHost(string ip) {
            // Ottiene path file immagine profilo host
            string filename = _referenceData.GetPathProfileImageHost(ip);

            // Se non esiste al momento sulla macchina locale non viene aggiornato
            if (filename.Equals("")) return;

            try {
                var file = File.OpenRead(filename);
                    
                file.Close();
                List<string> lista = new List<string>();
                foreach(var item in friendList.SelectedItems)
                    lista.Add(((Host)item).Ip);

                friendList.Items.Refresh();
                friendList.SelectedIndex = -1;

                foreach(var item in friendList.Items) {
                    if(lista.Contains(((Host)item).Ip) && ((Host)item).Status.Equals("online"))
                        friendList.SelectedItems.Add(item);
                }
            } catch (UnauthorizedAccessException e) {
                Console.WriteLine($"File not yet reciced : {e}");
            } catch (Exception e) {
                Console.WriteLine($"Exception on UpdateProfileHost - {e}");
            }
        }
        
        /// <summary>
        /// Aggiorna immagine del profilo (invia l'immagine)
        /// </summary>
        public async void SendProfileImage() {
            try {
                await _TCPSender.SendProfilePicture();
            }
            catch (Exception e) {
                Console.WriteLine($"Exception on SendProfileImage - {e}");
            }
        }

        /// <summary>
        /// Prepara file all'invio e invia messaggi di richiesta invio
        /// </summary>
        private async void ButtonSend_Click(object sender, RoutedEventArgs e) {
            List<string> currentSelectedHost = _referenceData.GetCurrentSelectedHost();
            List<string> currentPathToSend   = _referenceData.GetPathFileToSend();
            
            if (currentPathToSend.Count > 0 && currentSelectedHost.Count > 0) {

                List<string> pathFiles = new List<string>();
                Dictionary<string, FileSendStatus> listFile = new Dictionary<string, FileSendStatus>();

                // Crea un nuovo task per eseguire l'operazione di compressionev in modo asincrono
                await Task.Run(() => {
                    try {
                        string zipPath = @Utility.PathTmp() + "\\" + DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString() + "Files_" + _referenceData.GetLocalIPAddress().Replace(".","_") + "_.zip";
                        bool isFile = false;
                        
                        foreach (string path in currentPathToSend) {
                            FileAttributes fileAttributes = File.GetAttributes(path);
                            if (fileAttributes.HasFlag(FileAttributes.Directory)) {
                                string zipPathDir = @Utility.PathTmp() + "\\" + DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString() + "Dir_" + Path.GetFileName(path) + "_" +_referenceData.GetLocalIPAddress().Replace(".","_") + "_.zip";

                                listFile.Add(Utility.PathToFileName(zipPathDir), FileSendStatus.PREPARED);
                                ZipFile.CreateFromDirectory(path, zipPathDir);
                                listFile[Utility.PathToFileName(zipPathDir)] = FileSendStatus.READY;
                            }
                            else {
                                if (!File.Exists(zipPath)) {
                                    listFile.Add(Utility.PathToFileName(zipPath), FileSendStatus.PREPARED);

                                    using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create)) {
                                        archive.CreateEntryFromFile(path, Utility.PathToFileName(path));
                                        isFile = true;
                                    }
                                }
                                else {
                                    using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Update)) {
                                        archive.CreateEntryFromFile(path, Utility.PathToFileName(path));
                                    }
                                }
                            }

                        }
                        if (isFile)
                            listFile[Utility.PathToFileName(zipPath)] = FileSendStatus.READY;
                        
                        foreach (string ip in currentSelectedHost)
                            _referenceData.AddOrUpdateSendFile(ip, listFile);
                       
                        pathFiles = listFile.Keys.ToList();
                        _referenceData.ClearPathToSend(currentPathToSend);
                        obj.Release();
                    }catch(Exception ex) {
                        Console.WriteLine($"Exception on creation zip {ex}");
                    }
                });
                _referenceData.RemoveSelectedHosts(currentSelectedHost);
                await obj.WaitAsync();
                
                try {
                    await _TCPSender.SendRequest(pathFiles);
                }
                catch (Exception ex) {
                    Console.WriteLine($"Exception on ButtonSend_Click - {ex}");
                }
                    //NOME FILE SOPRA PROGRESS BAR
                    //string file_path = _referenceData.FileToSend.Keys.ToString();
                    //string file_name = Utility.PathToFileName(file_path);
                    //textFile.Text = file_name;
                    //textFile.Text = pathFiles[0];
            }
        }

        /// <summary>
        /// Annullamento selezione file da inviare
        /// </summary>
        private void ButtonUndo_Click(object sender, RoutedEventArgs e) {
            _referenceData.ClearPathFileToSend();
            textInfoMessage.Text = "";
            UndoButton.Visibility = Visibility.Hidden;
        }

        /// <summary>
        /// Metodo chiamato in caso di cambio selezione della lista amici
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void friendList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (e.AddedItems.Count > 0) {
                foreach (Host h in e.AddedItems) {
                    _referenceData.AddSelectedHost(h.Ip);
                    Console.WriteLine("UTENTE SELEZIONATO " + h.Ip);
                    if (h.Status.Equals("offline"))
                        friendList.SelectedItems.Remove(h);
                }
            }
            if (e.RemovedItems.Count > 0) {
                foreach (Host h in e.RemovedItems) {
                    _referenceData.RemoveSelectedHost(h.Ip);
                    Console.WriteLine("UTENTE DESELEZIONATO " + h.Ip);
                }
            }
            if(_referenceData.GetCurrentSelectedHost().Count > 0) {
                SendButton.Visibility = Visibility.Visible;
            } else
                SendButton.Visibility = Visibility.Hidden;
        }

        /// <summary>
        /// Metodo chiamato in caso di cambio deselezione di un host dalla lista amici
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FriendList_DoubleClick(object sender, MouseButtonEventArgs e) {
            ListBox list = (ListBox)sender;
            if (list.SelectedItems.Count == 1) {
                friendList.SelectedIndex = -1;
                SendButton.Visibility = Visibility.Hidden;
            }
        }
        
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
            string IpTAG = packetPart[packetPart.Length-5] + "." + packetPart[packetPart.Length - 4] + "." + packetPart[packetPart.Length - 3] + "." + packetPart[packetPart.Length - 2];

            SendResponse(fileName, IpTAG, PacketType.YFILE);

            yesButton.Visibility = Visibility.Hidden;
            noButton.Visibility = Visibility.Hidden;
            stopButton.Visibility = Visibility.Visible;

            flashTimer.Stop();
            ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_green.ico"));
            //main.StopFlashingWindow();
        }

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

            SendResponse(fileName, IpTAG, PacketType.NFILE);

            yesButton.Visibility = Visibility.Hidden;
            noButton.Visibility = Visibility.Hidden;
            stopButton.Visibility = Visibility.Hidden;

            flashTimer.Stop();
            ni.Icon = new System.Drawing.Icon(Utility.FileNameToSystem("share_green.ico"));
            //main.StopFlashingWindow();
        }
    }

}