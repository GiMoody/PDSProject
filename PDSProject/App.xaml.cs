using System;

using System.IO.Pipes;
using System.IO;

using System.Diagnostics;
using System.Windows;
using System.Runtime.InteropServices;
using System.Threading;


namespace PDSProject
{
    /// <summary>
    /// Logica di interazione per App.xaml
    /// </summary>
    public partial class App : Application
    {
        [DllImport("user32", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string cls, string win);
        [DllImport("user32")]
        private static extern IntPtr SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32")]
        private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32")]
        private static extern bool OpenIcon(IntPtr hWnd);

        static bool isMutexCreated = false;
        static Mutex mut = new Mutex(true, Process.GetCurrentProcess().ProcessName, out isMutexCreated);

        static bool isMutexCreatedPipe = false;
        static Mutex muPipe = new Mutex(false, Process.GetCurrentProcess().ProcessName + "Pipe", out isMutexCreatedPipe);
        
        protected override void OnStartup(StartupEventArgs e) {
            
            if (!isMutexCreated){
                string path = string.Join(" ", e.Args);
                PipeServer(path);
                ActivateOtherWindow();
                Environment.Exit(0);
            }
            else if(e.Args.Length > 0){
                string path = string.Join(" ", e.Args);
                PipeServerAsync(path);
            }
        }

        private static void ActivateOtherWindow() {
            var other = FindWindow(null, "Condividi");
            if (other != IntPtr.Zero) {
                SetForegroundWindow(other);
                if (IsIconic(other))
                    OpenIcon(other);
            }
        }

        private async void PipeServerAsync (string path) {
            try {
                muPipe.WaitOne();
                using (NamedPipeServerStream pipeServer =
                    new NamedPipeServerStream("PSDPipe", PipeDirection.Out)){
                    // Wait for a client to connect
                    await pipeServer.WaitForConnectionAsync();
                    // Read user input and send that to the client process.
                    using (StreamWriter sw = new StreamWriter(pipeServer))
                    {
                        //sw.AutoFlush = true;
                        await sw.WriteLineAsync(path);
                    }
                }
                muPipe.ReleaseMutex();
            }
            // Catch the IOException that is raised if the pipe is broken
            // or disconnected.
            catch (IOException e)
            {
                MessageBox.Show($"{DateTime.Now.ToString()}\t - PipeServer IOException: {e.Message}");
                Console.WriteLine($"{DateTime.Now.ToString()}\t - PipeServer IOException: {e.Message}");
            }
            catch (Exception e)
            {
                MessageBox.Show($"{DateTime.Now.ToString()}\t - PipeServer Exception - {e.GetType()} - {e.Message}");
                Console.WriteLine($"{DateTime.Now.ToString()}\t - PipeServer Exception - {e.GetType()} - {e.Message}");
            }
        }

        private void PipeServer(string path) {
            try {
                muPipe.WaitOne();
                using (NamedPipeServerStream pipeServer =
                    new NamedPipeServerStream("PSDPipe", PipeDirection.Out)) {
                    // Wait for a client to connect
                    pipeServer.WaitForConnection();
                    // Read user input and send that to the client process.
                    using (StreamWriter sw = new StreamWriter(pipeServer))
                    {
                        //sw.AutoFlush = true;
                        sw.WriteLine(path);
                    }
                }
                muPipe.ReleaseMutex();
            }
            // Catch the IOException that is raised if the pipe is broken
            // or disconnected.
            catch (IOException e) {
                MessageBox.Show($"{DateTime.Now.ToString()}\t - PipeServer IOException: {e.Message}");
                Console.WriteLine($"{DateTime.Now.ToString()}\t - PipeServer IOException: {e.Message}");
            }
            catch (Exception e)
            {
                MessageBox.Show($"{DateTime.Now.ToString()}\t - PipeServer Exception - {e.GetType()} - {e.Message}");
                Console.WriteLine($"{DateTime.Now.ToString()}\t - PipeServer Exception - {e.GetType()} - {e.Message}");
            }
        }
    }
}
    

