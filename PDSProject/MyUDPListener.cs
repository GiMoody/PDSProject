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
    /// Class that manage the UDPListener (server)
    /// </summary>
    class MyUDPListener
    {
        SharedInfo _referenceData;

        public MyUDPListener(){
            _referenceData = SharedInfo.Instance;
        }
        
        /// <summary>
        /// It listens all the UDP packets to excute the operation of Host discovery
        /// </summary>
        public async Task Listener (CancellationToken token) {
            UdpClient receiver = new UdpClient(SharedInfo.UDPReceivedPort);
            while (!token.IsCancellationRequested)
            {
                IPEndPoint receivedIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
                try {
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

        /// <summary>
        /// Serve the current UDP client
        /// </summary>
        async Task ServeClient(UdpReceiveResult result ) {
            try {
                byte[] receivedBytes = result.Buffer;
                IPEndPoint receivedIpEndPoint = result.RemoteEndPoint;

                MemoryStream stream = new MemoryStream(receivedBytes);
                stream.Position = 0;
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(Host));
                Host received = (Host)ser.ReadObject(stream);

                // Check if the remote user is not us
                if (_referenceData.GetListIps().Contains(receivedIpEndPoint.Address.ToString())) return;

                // Update network information if needed
                if (_referenceData.UpdateNetworkConfiguration(receivedIpEndPoint.Address.ToString())) {
                    if (!_referenceData.isFirst) {
                        await MainWindow.main.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                            MainWindow.main.StopTCPListener();
                        }));
                    }
                    else
                        _referenceData.isFirst = false;

                    // Pulse monitor to advise the TCPlistener to start
                    lock (_referenceData.cvListener) {
                        Monitor.Pulse(_referenceData.cvListener);
                    }
                }

                // Update user data if needed
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