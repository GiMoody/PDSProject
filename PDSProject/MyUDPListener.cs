using System;

using System.IO;

using System.Net;
using System.Net.Sockets;

using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

using System.Threading;
using System.Threading.Tasks;

using System.Windows.Threading;

namespace PDSProject
{
    /// <summary>
    /// Classe che gestisce l'UDPListener, ovvero il server.
    /// Sta in ascolto per ricevere profilo dei vari utenti connessi alla rete locale.
    /// </summary>
    class MyUDPListener
    {
        SharedInfo _referenceData;

        public MyUDPListener(){
            _referenceData = SharedInfo.Instance;
        }
        
        /// <summary>
        /// Si mette in ascolto di pachetti UDP per eseguire l'operazione di Host discovery
        /// </summary>
        public async Task Listener (CancellationToken token) {
            UdpClient receiver = new UdpClient(_referenceData.UDPReceivedPort);
            while (!token.IsCancellationRequested)
            {
                IPEndPoint receivedIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
                try {
                    //var result = 
                    await receiver.ReceiveAsync().WithWaitCancellation(token).ContinueWith(async ( antecedent ) => {
                        try {
                            await ServeClient((UdpReceiveResult)antecedent.Result);
                        }
                        catch (Exception e) {
                            Console.WriteLine($"{DateTime.Now.ToString()}\t - UDPListener Exception {e.Message}");
                        }
                    }, TaskContinuationOptions.OnlyOnRanToCompletion);
                }
                catch (ObjectDisposedException) when (token.IsCancellationRequested) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - UDPListener stopped listening because cancellation was requested.");
                }
                catch (AggregateException ae) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - UDPListener AggregateException: {ae.Message}");
                }
                catch (OperationCanceledException oce) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - UDPListener OperationCanceledException: {oce.Message}");
                }
                catch(SerializationException se) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - UDPListener SerializationException - UDP Packet is in the wrong format: {se.Message}");
                }
                catch (SocketException ex) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - UDPListener SocketException: {ex.Message}");
                }
                catch (Exception ex) {
                    Console.WriteLine($"{DateTime.Now.ToString()}\t - UDPListener Exception handling client: {ex.GetType()} - {ex.Message}");
                }
            }
            receiver.Close();
        }

        async Task ServeClient(UdpReceiveResult result ) {
            try {
                byte[] receivedBytes = result.Buffer;
                IPEndPoint receivedIpEndPoint = result.RemoteEndPoint;

                MemoryStream stream = new MemoryStream(receivedBytes);
                stream.Position = 0;
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(Host));
                Host received = (Host)ser.ReadObject(stream);

                // Controlla che il mittente non siamo noi
                if (_referenceData.LocalIps.Contains(receivedIpEndPoint.Address.ToString())) return;

                // Nel caso ci sia stat un cambio di rete avvisa in questo modo
                if (_referenceData.UpdateNetworkConfiguration(receivedIpEndPoint.Address.ToString())) {
                    if (!_referenceData.isFirst) {
                        await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                            MainWindow.main.StopTCPListener();
                        }));
                    }
                    else
                        _referenceData.isFirst = false;

                    // Avviso il TCPListener di attivarsi
                    lock (_referenceData.cvListener) {
                        Monitor.Pulse(_referenceData.cvListener);
                    }
                }
                if (_referenceData.UpdateUsersInfo(received, receivedIpEndPoint.Address.ToString())) {
                    string ip = receivedIpEndPoint.Address.ToString();
                    await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                        MainWindow.main.UpdateProfileHost(ip);
                        MainWindow.main.UpdateHostName(ip, received.Name);
                        MainWindow.main.SendProfileImage();
                    }));
                }
            }
            catch (AggregateException ae) {
                Console.WriteLine($"{DateTime.Now.ToString()}\t - UDPListener AggregateException: {ae.Message}");
            }
            catch (SerializationException se) {
                Console.WriteLine($"{DateTime.Now.ToString()}\t - UDPListener SerializationException - UDP Packet is in the wrong format: {se.Message}");
            }
            catch (Exception se) {
                Console.WriteLine($"{DateTime.Now.ToString()}\t - UDPListener Exception - {se.Message}");
            }
        }       
    }
}