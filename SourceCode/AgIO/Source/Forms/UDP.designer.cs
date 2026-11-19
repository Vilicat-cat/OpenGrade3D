using System;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using AgLibrary.Logging;

namespace AgIO
{
    public class CTraffic
    {     
        public int cntrGPSIn = 0;
        public int cntrGPSInBytes = 0;
        public int cntrGPSOut = 0;

        // Parche integración OG3D: tráfico de la fuente GPS-2 separada (módulo OpenGrade3D).
        // cntrGPSOG3D    = tramas 0x6A/0xD1 (GNSS del módulo OG3D, hoy la antena; mañana el GPS de la cuchilla)
        // cntrBladeOG3D  = tramas 0x6A/0xE1 (datos cuchilla) y 0x6A/0xE2 (telemetría)
        public int cntrGPSOG3D = 0;
        public int cntrBladeOG3D = 0;

        public uint helloFromMachine = 99, helloFromAutoSteer = 99, helloFromIMU = 99;
    }

    public class CScanReply
    {
        public string steerIP =   "";
        public string machineIP = "";
        public string GPS_IP =    "";
        public string IMU_IP =    "";
        public string subnetStr = "";

        public byte[] subnet = { 0, 0, 0 };

        public bool isNewSteer, isNewMachine, isNewGPS, isNewIMU;

        public bool isNewData = false;
    }

    public partial class FormLoop
    {
        // loopback Socket
        private Socket loopBackSocket;
        private EndPoint endPointLoopBack = new IPEndPoint(IPAddress.Loopback, 0);

        // UDP Socket
        public Socket UDPSocket;
        private EndPoint endPointUDP = new IPEndPoint(IPAddress.Any, 0);
        
        public bool isUDPNetworkConnected;

        //2 endpoints for local and 2 udp

        private IPEndPoint epAgOpen = new IPEndPoint(IPAddress.Parse(
            Properties.Settings.Default.eth_loopOne.ToString() + "." +
            Properties.Settings.Default.eth_loopTwo.ToString() + "." +
            Properties.Settings.Default.eth_loopThree.ToString() + "." +
            Properties.Settings.Default.eth_loopFour.ToString()), 15555);

        // Parche integración OG3D: segundo destino de bucle para OpenGrade3D.
        // Mismos PGN (0x7C 0xD6 GPS, binario del módulo, etc.); OpenGrade3D escucha en 15556
        // para poder convivir con AgOpenGPS (que sigue usando 15555) en la misma tablet.
        private IPEndPoint epOG3D = new IPEndPoint(IPAddress.Parse(
            Properties.Settings.Default.eth_loopOne.ToString() + "." +
            Properties.Settings.Default.eth_loopTwo.ToString() + "." +
            Properties.Settings.Default.eth_loopThree.ToString() + "." +
            Properties.Settings.Default.eth_loopFour.ToString()), 15556);
        
        public IPEndPoint epModule = new IPEndPoint(IPAddress.Parse(
                Properties.Settings.Default.etIP_SubnetOne.ToString() + "." +
                Properties.Settings.Default.etIP_SubnetTwo.ToString() + "." +
                Properties.Settings.Default.etIP_SubnetThree.ToString() + ".255"), 8888);
        private IPEndPoint epNtrip;

        public IPEndPoint epModuleSet = new IPEndPoint(IPAddress.Parse("255.255.255.255"), 8888);
        public byte[] ipAutoSet = { 192, 168, 5 };

        //class for counting bytes
        public CTraffic traffic = new CTraffic();
        public CScanReply scanReply = new CScanReply();

        //scan results placed here
        public string scanReturn = "Scanning...";
        
        // Data stream
        private byte[] buffer = new byte[1024];

        //used to send communication check pgn= C8 or 200
        private byte[] helloFromAgIO = { 0x80, 0x81, 0x7F, 200, 3, 56, 0, 0, 0x47 };

        public IPAddress ipCurrent;
        //initialize loopback and udp network
        public void LoadUDPNetwork()
        {
            helloFromAgIO[5] = 56;

            lblIP.Text = "";
            try //udp network
            {
                foreach (IPAddress IPA in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (IPA.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string  data = IPA.ToString();
                        lblIP.Text += IPA.ToString().Trim() + "\r\n";
                    }
                }

                // Initialise the socket
                UDPSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                UDPSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                UDPSocket.Bind(new IPEndPoint(IPAddress.Any, 9999));
                UDPSocket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref endPointUDP,
                    new AsyncCallback(ReceiveDataUDPAsync), null);

                isUDPNetworkConnected = true;

                if (isUDPNetworkConnected)
                {
                    Log.EventWriter("UDP Network is connected: " + epModule.ToString());
                }
                else
                {
                    Log.EventWriter("UDP Network Failed to Connect");
                }

                btnUDP.BackColor = Color.LimeGreen;

                //if (!isFound)
                //{
                //    MessageBox.Show("Network Address of Modules -> " + Properties.Settings.Default.setIP_localAOG+"[2 - 254] May not exist. \r\n"
                //    + "Are you sure ethernet is connected?\r\n" + "Go to UDP Settings to fix.\r\n\r\n", "Network Connection Error",
                //    MessageBoxButtons.OK, MessageBoxIcon.Error);
                //    //btnUDP.BackColor = Color.Red;
                //    lblIP.Text = "Not Connected";
                //}
            }
            catch (Exception e)
            {
                Log.EventWriter("Catch -> Load UDP Server" + e);
                MessageBox.Show(e.Message, "Serious Network Connection Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnUDP.BackColor = Color.Red;
                lblIP.Text = "Error";
            }
        }

        private void LoadLoopback()
        { 
            try //loopback
            {
                loopBackSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                loopBackSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                loopBackSocket.Bind(new IPEndPoint(IPAddress.Loopback, 17777));
                loopBackSocket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref endPointLoopBack, 
                    new AsyncCallback(ReceiveDataLoopAsync), null);
                Log.EventWriter("Loopback is Connected: " + IPAddress.Loopback.ToString() + ":17777");

            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch - > Load UDP Loopback Failed: " + ex.ToString());
                MessageBox.Show("Load Error: " + ex.Message, "Loopback Server", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #region Send LoopBack

        private void SendToLoopBackMessageAOG(byte[] byteData)
        {
            SendDataToLoopBack(byteData, epAgOpen);
            // Parche integración OG3D: envío duplicado hacia OpenGrade3D (15556).
            // PGNs intactos; cada app ignora los PGN que no le pertenecen.
            SendDataToLoopBack(byteData, epOG3D);
        }

        // Parche integración OG3D: envío de PGNs de posición con selección de fuente
        // (botón "Fuente GPS" del recuadro OG3D, ajuste setOG3D_gpsFromAOG).
        // fromAOG=true  -> PGN 0x7C/0xD6 (GPS de AOG reconstruido por AgIO o reenviado del módulo GPS).
        // fromAOG=false -> PGN 0x6A/0xD1 (GNSS propio del módulo OpenGrade3D).
        // AgOpenGPS (15555) recibe siempre ambos; OpenGrade3D (15556) solo la fuente elegida,
        // así ninguna app mezcla dos corrientes GPS distintas.
        private void SendGpsToLoopBack(byte[] byteData, bool fromAOG)
        {
            SendDataToLoopBack(byteData, epAgOpen);
            if (fromAOG == Properties.Settings.Default.setOG3D_gpsFromAOG)
            {
                SendDataToLoopBack(byteData, epOG3D);
            }
        }

        private void SendDataToLoopBack(byte[] byteData, IPEndPoint endPoint)
        {
            try
            {
                if (byteData.Length != 0)
                {
                    // Send packet to AgVR
                    loopBackSocket.BeginSendTo(byteData, 0, byteData.Length, SocketFlags.None, endPoint,
                        new AsyncCallback(SendDataLoopAsync), null);
                }
            }
            catch
            {
            }
        }

        public void SendDataLoopAsync(IAsyncResult asyncResult)
        {
            try
            {
                loopBackSocket.EndSend(asyncResult);
            }
            catch
            {
            }
        }

        #endregion

        #region Receive LoopBack

        private void ReceiveFromLoopBack(byte[] data)
        {
            //Send out to udp network
            SendUDPMessage(data, epModule);

            if (data[0] == 0x80 && data[1] == 0x81)
            {
                switch (data[3])
                {
                    case 0xFE: //254 AutoSteer Data
                        {
                            //serList.AddRange(data);
                            SendSteerModulePort(data, data.Length);
                            SendMachineModulePort(data, data.Length);
                            break;
                        }
                    case 0xEF: //239 machine pgn
                        {
                            SendMachineModulePort(data, data.Length);
                            SendSteerModulePort(data, data.Length);
                            break;
                        }
                    case 0xE5: //229 Symmetric Sections - Zones
                        {
                            SendMachineModulePort(data, data.Length);
                            //SendSteerModulePort(data, data.Length);
                            break;
                        }
                    case 0xFC: //252 steer settings
                        {
                            SendSteerModulePort(data, data.Length);
                            break;
                        }
                    case 0xFB: //251 steer config
                        {
                            SendSteerModulePort(data, data.Length);
                            break;                        }

                    case 0xEE: //238 machine config
                        {
                            SendMachineModulePort(data, data.Length);
                            SendSteerModulePort(data, data.Length);
                            break;                        }

                    case 0xEC: //236 machine config
                        {
                            SendMachineModulePort(data, data.Length);
                            SendSteerModulePort(data, data.Length);
                            break;
                        }

                    case 0xBA: //186 blade setpoint (it13.7): altitud de corte mm + demanda de válvula + offset
                    case 0xB8: //184 blade settings (it13.7): PWM gain/min/max por dirección + deadband
                        {
                            SendMachineModulePort(data, data.Length);
                            break;
                        }
                }
            }                            
        }

        private void ReceiveDataLoopAsync(IAsyncResult asyncResult)
        {
            try
            {
                // Receive all data
                int msgLen = loopBackSocket.EndReceiveFrom(asyncResult, ref endPointLoopBack);

                byte[] localMsg = new byte[msgLen];
                Array.Copy(buffer, localMsg, msgLen);

                // Listen for more connections again...
                loopBackSocket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref endPointLoopBack, 
                    new AsyncCallback(ReceiveDataLoopAsync), null);

                BeginInvoke((MethodInvoker)(() => ReceiveFromLoopBack(localMsg)));
            }
            catch
            {
            }
        }

        #endregion

        #region Send UDP

        public void SendUDPMessage(byte[] byteData, IPEndPoint endPoint)
        {
            if (isUDPNetworkConnected)
            {
                if (isUDPMonitorOn)
                {
                    if (epNtrip != null && endPoint.Port == epNtrip.Port)
                    {
                        if (isNTRIPLogOn)
                            logUDPSentence.Append(DateTime.Now.ToString("ss.fff\t") + endPoint.ToString() + "\t" + " > NTRIP\r\n");
                    }
                    else
                    {
                        logUDPSentence.Append(DateTime.Now.ToString("ss.fff\t") + endPoint.ToString() + "\t" + " > " + byteData[3].ToString() + "\r\n");
                    }
                }

                try
                {
                    // Send packet to the zero
                    if (byteData.Length != 0)
                    {
                        UDPSocket.BeginSendTo(byteData, 0, byteData.Length, SocketFlags.None,
                           endPoint, new AsyncCallback(SendDataUDPAsync), null);
                    }
                }
                catch (Exception)
                {
                    //WriteErrorLog("Sending UDP Message" + e.ToString());
                    //MessageBox.Show("Send Error: " + e.Message, "UDP Client", MessageBoxButtons.OK,
                    //MessageBoxIcon.Error);
                }
            }
        }

        private void SendDataUDPAsync(IAsyncResult asyncResult)
        {
            try
            {
                UDPSocket.EndSend(asyncResult);
            }
            catch
            {
            }
        }

        #endregion

        #region Receive UDP

        private void ReceiveDataUDPAsync(IAsyncResult asyncResult)
        {
            try
            {
                // Receive all data
                int msgLen = UDPSocket.EndReceiveFrom(asyncResult, ref endPointUDP);

                byte[] localMsg = new byte[msgLen];
                Array.Copy(buffer, localMsg, msgLen);

                // Listen for more connections again...
                UDPSocket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref endPointUDP, 
                    new AsyncCallback(ReceiveDataUDPAsync), null);

                BeginInvoke((MethodInvoker)(() => ReceiveFromUDP(localMsg)));

            }
            catch 
            {
            }
        }

        private void ReceiveFromUDP(byte[] data)
        {
            try
            {
                if (data[0] == 0x80 && data[1] == 0x81)
                {
                    // Parche integración OG3D: los PGNs de posición (0x7C/0xD6 GPS de AOG y
                    // 0x6A/0xD1 GNSS del módulo OG3D) solo llegan a OpenGrade3D (15556) cuando
                    // esa es la fuente marcada en el botón del recuadro OG3D; el resto de PGNs
                    // se reenvía igual a ambos loopbacks (15555 AOG + 15556 OG3D).
                    if (data[2] == 0x7C && data[3] == 0xD6)
                    {
                        SendGpsToLoopBack(data, true);
                    }
                    else if (data[2] == 0x6A && data[3] == 0xD1)
                    {
                        SendGpsToLoopBack(data, false);
                    }
                    else
                    {
                        //module return via udp sent to AOG
                        SendToLoopBackMessageAOG(data);
                    }

                    // Parche integración OG3D: métricas de las fuentes GPS separadas.
                    // El Teensy de OpenGrade3D manda al 9999 su familia 0x6A:
                    //   0x6A 0xD1 -> GNSS del módulo OG3D (fuente GPS-2 del recuadro OG3D)
                    //   0x6A 0xE1 -> datos de la cuchilla | 0x6A 0xE2 -> telemetría
                    // Se sigue reenviando todo al loopback intacto (15555 AOG + 15556 OG3D).
                    if (data[2] == 0x6A)
                    {
                        if (data[3] == 0xD1)
                        {
                            traffic.cntrGPSOG3D++;
                            ParseOG3DGpsPacket(data);
                        }
                        else if (data[3] == 0xE1 || data[3] == 0xE2)
                        {
                            traffic.cntrBladeOG3D++;
                        }
                    }
                    //check for Scan and Hello
                    if (data[3] == 126 && data.Length == 11)
                    {

                        traffic.helloFromAutoSteer = 0;
                        if (isViewAdvanced)
                        {
                            lblPing.Text = (((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds - pingSecondsStart) * 1000).ToString("N0");
                            double actualSteerAngle = (Int16)((data[6] << 8) + data[5]);
                            lblSteerAngle.Text = (actualSteerAngle * 0.01).ToString("N1");
                            lblWASCounts.Text = ((Int16)((data[8] << 8) + data[7])).ToString();

                            lblSwitchStatus.Text = ((data[9] & 2) == 2).ToString();
                            lblWorkSwitchStatus.Text = ((data[9] & 1) == 1).ToString();
                        }
                    }

                    else if (data[3] == 123 && data.Length == 11)
                    {

                        traffic.helloFromMachine = 0;

                        if (isViewAdvanced)
                        {
                            lblPingMachine.Text = (((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds - pingSecondsStart) * 1000).ToString("N0");
                            lbl1To8.Text = Convert.ToString(data[5], 2).PadLeft(8, '0');
                            lbl9To16.Text = Convert.ToString(data[6], 2).PadLeft(8, '0');
                        }
                    }

                    else if (data[3] == 121 && data.Length == 11)
                        traffic.helloFromIMU = 0;

                    //scan Reply
                    else if (data[3] == 203 && data.Length == 13) //
                    {
                        if (data[2] == 126)  //steer module
                        {
                            scanReply.steerIP = data[5].ToString() + "." + data[6].ToString() + "." + data[7].ToString() + "." + data[8].ToString();

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString() + "." + data[10].ToString() + "." + data[11].ToString();

                            scanReply.isNewData = true;
                            scanReply.isNewSteer = true;
                        }
                        //
                        else if (data[2] == 123)   //machine module
                        {
                            scanReply.machineIP = data[5].ToString() + "." + data[6].ToString() + "." + data[7].ToString() + "." + data[8].ToString();

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString() + "." + data[10].ToString() + "." + data[11].ToString();

                            scanReply.isNewData = true;
                            scanReply.isNewMachine = true;

                        }
                        else if (data[2] == 121)   //IMU Module
                        {
                            scanReply.IMU_IP = data[5].ToString() + "." + data[6].ToString() + "." + data[7].ToString() + "." + data[8].ToString();

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString() + "." + data[10].ToString() + "." + data[11].ToString();

                            scanReply.isNewData = true;
                            scanReply.isNewIMU = true;
                        }

                        else if (data[2] == 120)    //GPS module
                        {
                            scanReply.GPS_IP = data[5].ToString() + "." + data[6].ToString() + "." + data[7].ToString() + "." + data[8].ToString();

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString() + "." + data[10].ToString() + "." + data[11].ToString();

                            scanReply.isNewData = true;
                            scanReply.isNewGPS = true;
                        }
                    }

                    if (isUDPMonitorOn)
                    {
                        logUDPSentence.Append(DateTime.Now.ToString("ss.fff\t") + endPointUDP.ToString() + "\t" + " < " + data[3].ToString() + "\r\n");
                    }

                } // end of pgns

                else if (data[0] == 36 && (data[1] == 71 || data[1] == 80 || data[1] == 75))
                {
                    traffic.cntrGPSOut += data.Length;
                    rawBuffer += Encoding.ASCII.GetString(data);
                    ParseNMEA(ref rawBuffer);

                    if (isUDPMonitorOn && isGPSLogOn)
                    {
                        logUDPSentence.Append(DateTime.Now.ToString("ss.fff\t") + System.Text.Encoding.ASCII.GetString(data));
                    }
                }
            }
            catch
            {
            }
        }

        #region Parche integración OG3D - fuente GPS-2 separada

        // Posición de la fuente GPS-2 (PGN 0x6A/0xD1, formato OpenGrade):
        // [5..12]  lon int64 x1e9 en DDMM.MMMM | [13..20] lat int64 x1e9 en DDMM.MMMM
        // [45] fixQuality | [46] satélites. Misma decodificación que la app OpenGrade3D.

        private void ParseOG3DGpsPacket(byte[] data)
        {
            try
            {
                if (data.Length < 47) return;

                double nmeaLon = BitConverter.ToInt64(data, 5) / 1000000000.0;
                double nmeaLat = BitConverter.ToInt64(data, 13) / 1000000000.0;

                // NMEA DDMM.MMMM -> grados decimales
                double lonDeg = Math.Truncate(nmeaLon / 100.0);
                double lon = lonDeg + (nmeaLon - lonDeg * 100.0) / 60.0;
                double latDeg = Math.Truncate(nmeaLat / 100.0);
                double lat = latDeg + (nmeaLat - latDeg * 100.0) / 60.0;

                // Descarta valores centinela (sin fix)
                if (Math.Abs(lon) > 181.0 || Math.Abs(lat) > 91.0) return;

                lblGPS2Lat.Text = "Lat: " + lat.ToString("N7");
                lblGPS2Lon.Text = "Lon: " + lon.ToString("N7");
            }
            catch
            {
            }
        }

        #endregion

        #endregion
    }
}
