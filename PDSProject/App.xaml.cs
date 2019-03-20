using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Runtime.InteropServices;
using System.IO.Pipes;
using System.IO;

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

        protected override void OnStartup(StartupEventArgs e)
        {

            if (Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Length > 1)
            {
                string path = string.Join("", e.Args);
                PipeServer(path);
                //ActivateOtherWindow();
                //Current.Dispatcher.BeginInvoke((Action)(() => ((MainWindow)Current.MainWindow).Test(e.Args)));
                Shutdown();
            }
        }

        private static void ActivateOtherWindow()
        {
            var other = FindWindow(null, "Condividi");
            if (other != IntPtr.Zero)
            {
                SetForegroundWindow(other);
                if (IsIconic(other))
                    OpenIcon(other);
            }
        }

        private void PipeServer(string path)
        {
            using (NamedPipeServerStream pipeServer =
                new NamedPipeServerStream("PSDPipe", PipeDirection.Out))
            {
                Console.WriteLine("NamedPipeServerStream object created.");

                // Wait for a client to connect
                Console.Write("Waiting for client connection...");
                pipeServer.WaitForConnection();

                Console.WriteLine("Client connected.");
                try
                {
                    // Read user input and send that to the client process.
                    using (StreamWriter sw = new StreamWriter(pipeServer))
                    {
                        sw.AutoFlush = true;
                        sw.WriteLine(path);
                    }
                }
                // Catch the IOException that is raised if the pipe is broken
                // or disconnected.
                catch (IOException e)
                {
                    Console.WriteLine("ERROR: {0}", e.Message);
                }
            }
        }
    }
    
}
