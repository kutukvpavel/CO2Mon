using System;
using System.Collections.Generic;
using System.Timers;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

using RJCP.IO.Ports;
using ScottPlot;
using Avalonia.Data;
using Avalonia.Threading;

using Timer = System.Timers.Timer;

namespace CO2Mon.Models
{
    public enum MH_Z19_Req_Structure
    {
        Address = 0,
        RequestPrefix,
        Command,
        ArgumentBegin,
        CRC = 8
    }
    public enum MH_Z19_Resp_Structure
    {
        Address = 0,
        Command,
        ArgumentBegin,
        CRC = 8
    }
    public enum MH_Z19B_Commands : byte
    {
        RecoveryReset = 0x78,	// 0 Recovery Reset        Changes operation mode and performs MCU reset
        SetABC = 0x79,	// 1 ABC (Automatic Baseline Correction) Mode ON/OFF - Turns ABC logic on or off (b[3] == 0xA0 - on, 0x00 - off)
        GetABC = 0x7D,	// 2 Get ABC logic status  (1 - enabled, 0 - disabled)
        GetRaw = 0x84,	// 3 Raw CO2
        GetUnlimited = 0x85,	// 4 Temperature float, CO2 Unlimited
        GetLimited = 0x86,	// 5 Temperature integer, CO2 limited / clipped
        CalibrateOffset = 0x87,	// 6 Zero Calibration
        CalibrateSpan = 0x88,	// 7 Span Calibration
        SetRange = 0x99,	// 8 Range
        GetRange = 0x9B,	// 9 Get Range
        GetBackground = 0x9C,	// 10 Get Background CO2
        GetFWVer = 0xA0,	// 11 Get Firmware Version
        RepeatLastResp = 0xA2,	// 12 Get Last Response
        GetTempCal = 0xA3	// 13 Get Temperature Calibration
    }

    public class MH_Z19B_DataPoint
    {
        public MH_Z19B_DataPoint(DateTime dt, ushort u, ushort l)
        {
            Timestamp = dt;
            UnlimitedCO2 = u;
            LimitedCO2 = l;
        }

        public DateTime Timestamp { get; }
        public ushort UnlimitedCO2 { get; }
        public ushort LimitedCO2 { get; }
    }

    public class MH_Z19B
    {
        public const int PacketLength = 9; //Bytes
        public const int MaxRequestArgs = MH_Z19_Req_Structure.CRC - MH_Z19_Req_Structure.ArgumentBegin;

        public static int BaudRate { get; set; } = 9600;
        public static int PollInterval { get; set; } = 1000; //mS
        public static int Timeout { get; set; } = 3000; //mS
#warning Change the following default value to something larger for production
        public static int InitialCapacity { get; set; } = 10; //Points per channel

        public static event EventHandler<string>? OnLog;
        private static void Log(object? sender, string s)
        {
            OnLog?.Invoke(sender, s);
        }
        public static byte ComputeCrc(ReadOnlySpan<byte> buffer)
        {
            byte crc = 0;
            for (int i = 1; i < (PacketLength - 1); i++)
            {
                crc += buffer[i];
            }
            crc = (byte)(0xFFu - crc);
            return ++crc;
        }

        public MH_Z19B(string portName) : this(new SerialPortStream(portName, BaudRate) 
        {
            WriteTimeout = Timeout,
            ReadTimeout = Timeout
        }) { }
        public MH_Z19B(SerialPortStream p)
        {
            Port = p;
            timPoll.Elapsed += timPoll_Elapsed;
        }

        public event EventHandler<MH_Z19B_DataPoint>? OnNewDataReceived;

        //Private
        private readonly Timer timPoll = new(PollInterval) { AutoReset = true, Enabled = false };
        private CancellationTokenSource tknSource = new();

        private void timPoll_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (Port.IsOpen)
            {
                Poll().Wait();
            }
            else //Try to reconnect after serial adapter disconnection
            {
                Connect().Wait();
            }
        }
        private async Task Poll()
        {
            if (!BitConverter.IsLittleEndian) throw new NotImplementedException("Only LE archs are supported.");

            var dt = DateTime.Now;
            try
            {
                var x = dt.ToOADate(); 

                var limited = await ExchageData((byte)MH_Z19B_Commands.GetLimited);
                var l = BitConverter.ToUInt16(limited);
                PointsLimited.Add(new Coordinates(x, l));

                var unlim = await ExchageData((byte)MH_Z19B_Commands.GetUnlimited);
                var u = BitConverter.ToUInt16(unlim, 2);
                PointsUnlimited.Add(new Coordinates(x, u));

                Dispatcher.UIThread.Post(() => { 
                    OnNewDataReceived?.Invoke(this, new MH_Z19B_DataPoint(dt, u, l)); 
                });
            }
            catch (Exception ex)
            {
                Log(this, $"Failed to poll the device: {ex}");
            }
        }
        private async Task<bool> VerifyConnection()
        {
            try
            {
                byte[] first = await ExchageData((byte)MH_Z19B_Commands.GetUnlimited);
                byte[] second = await ExchageData((byte)MH_Z19B_Commands.RepeatLastResp);
                return first.SequenceEqual(second);
            }
            catch (DataValidationException)
            {
                return false;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }
        private async Task<byte[]> ExchageData(byte cmd, params byte[] args)
        {
            if ((args?.Length ?? 0) > MaxRequestArgs) throw new ArgumentOutOfRangeException(nameof(args));

            //Construct command
            byte[] request = new byte[PacketLength];
            request[(int)MH_Z19_Req_Structure.Address] = 0xFF; //Broadcast packet
            request[(int)MH_Z19_Req_Structure.RequestPrefix] = 0x01; //Inbound control command prefix
            request[(int)MH_Z19_Req_Structure.Command] = cmd;
            args?.CopyTo(request, (int)MH_Z19_Req_Structure.ArgumentBegin);
            request[(int)MH_Z19_Req_Structure.CRC] = ComputeCrc(request);

            //Send and wait for a response
            await Port.WriteAsync(request, 0, request.Length, tknSource.Token);
            byte[] response = new byte[PacketLength];
            int read = await Port.ReadAsync(response, 0, response.Length, tknSource.Token);

            //Check CRC
            if (read < PacketLength) throw new TimeoutException();
            byte crcIn = ComputeCrc(response);
            if (request[(int)MH_Z19_Req_Structure.CRC] != crcIn)
            {
                Log(this, $"CRC Error: out = {request[(int)MH_Z19_Req_Structure.CRC]:X2}, in = {crcIn:X2}");
                throw new DataValidationException(null);
            }

            //Cut out the argument to be returned
            return response.Take((int)MH_Z19_Resp_Structure.CRC).Skip((int)MH_Z19_Resp_Structure.ArgumentBegin).ToArray();
        }

        //Public
        public SerialPortStream Port { get; }
        public List<Coordinates> PointsLimited { get; } = new List<Coordinates>(InitialCapacity);
        public List<Coordinates> PointsUnlimited { get; } = new List<Coordinates>(InitialCapacity);
        public bool IsConnected { get => Port.IsOpen && !tknSource.IsCancellationRequested; }
        public bool IsPolling { get => timPoll.Enabled; }

        public async Task Connect()
        {
            if (IsConnected) throw new InvalidOperationException("Already connected");
            if (tknSource.IsCancellationRequested) tknSource = new CancellationTokenSource();
            try
            {
                Port.Open();
                if (await VerifyConnection()) 
                {
                    Log(this, "Found the device.");
                }
                else
                {
                    Port.Close();
                    Log(this, "Device NOT found!");
                } 
            }
            catch (Exception ex)
            {
                Log(this, $"Failed to connect to the device: {ex}");
            }
        }
        public void Disconnect()
        {
            if (!IsConnected) throw new InvalidOperationException("Already closed.");
            if (IsPolling) StopPoll();
            if (!tknSource.IsCancellationRequested) tknSource.Cancel();
            try
            {
                Port.Close();
            }
            catch (Exception ex)
            {
                Log(this, $"Failed to disconnect from the device: {ex}");
            }
        }
        public void StartPoll()
        {
            if (IsPolling) throw new InvalidOperationException("Already polling.");
            if (!IsConnected) throw new InvalidOperationException("Unable to poll the device, the port is closed.");

            timPoll.Start();
        }
        public void StopPoll()
        {
            if (!IsPolling) throw new InvalidOperationException("Already idling.");

            timPoll.Stop();
        }
        public async Task<byte[]> ExecuteCommand(byte cmd, params byte[] args)
        {
            if (!IsConnected) throw new InvalidOperationException("Unable to send a command to an unconnected device.");
            if (IsPolling) throw new InvalidOperationException("Unable to send a custom command during continuous polling.");

            return await ExchageData(cmd, args);
        }
    }
}