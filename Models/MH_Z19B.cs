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
        public MH_Z19B_DataPoint(DateTime dt, ushort u, ushort l, ushort r)
        {
            Timestamp = dt;
            UnlimitedCO2 = u;
            LimitedCO2 = l;
            RawCO2 = r;
        }

        public DateTime Timestamp { get; }
        public ushort UnlimitedCO2 { get; }
        public ushort LimitedCO2 { get; }
        public ushort RawCO2 { get; }
    }

    public class MH_Z19B : IDisposable
    {
        public const int PacketLength = 9; //Bytes
        public const int MaxRequestArgs = MH_Z19_Req_Structure.CRC - MH_Z19_Req_Structure.ArgumentBegin;

        public static bool AutoPoll {get;set;} = true;
        public static int BaudRate { get; set; } = 9600;
        public static int PollInterval { get; set; } = 1000; //mS
        public static int Timeout { get; set; } = 5000; //mS
        public static int InitialCapacity { get; set; } = 7200; //Points per channel
        public static int ConnectionFailureLimit { get; set; } = 5;
        public static bool AutoDisableABC {get;set;} = true;

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
        public event EventHandler? OnConnected;
        public event EventHandler? OnDisconnected;

        //Private
        private readonly Timer timPoll = new(PollInterval) { AutoReset = true, Enabled = false };
        private CancellationTokenSource tknSource = new();
        private int connectionFailures = 0;

        private void timPoll_Elapsed(object? sender, ElapsedEventArgs e)
        {
            lock (timPoll)
            {
                if (tknSource.IsCancellationRequested) return;
                if (Port.IsOpen)
                {
                    connectionFailures = 0;
                    Poll().Wait();
                }
                else //Try to reconnect after serial adapter disconnection
                {
                    if (connectionFailures++ < ConnectionFailureLimit || ConnectionFailureLimit < 0) Connect().Wait();
                    else Disconnect(false);
                }
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
                var l = BitConverter.ToUInt16(limited.Take(2).Reverse().ToArray());
                PointsLimited.Add(new Coordinates(x, l));

                var unlim = await ExchageData((byte)MH_Z19B_Commands.GetUnlimited);
                var u = BitConverter.ToUInt16(unlim.Skip(2).Take(2).Reverse().ToArray());
                PointsUnlimited.Add(new Coordinates(x, u));

                var raw = await ExchageData((byte)MH_Z19B_Commands.GetRaw);
                var r = BitConverter.ToUInt16(raw.Take(2).Reverse().ToArray());
                PointsRaw.Add(new Coordinates(x, r));

                OnNewDataReceived?.Invoke(this, new MH_Z19B_DataPoint(dt, u, l, r));
            }
            catch (Exception ex)
            {
                Log(this, $"Failed to poll the device: {ex}");
            }
        }
        private async Task<bool> VerifyConnection()
        {
            Log(this, "Connection verification...");
            try
            {
                Log(this, "\tSending GetUnlim request...");
                byte[] first = await ExchageData((byte)MH_Z19B_Commands.GetUnlimited);
                Log(this, "\tSending RepeatLastResponse request...");
                byte[] second = await ExchageData((byte)MH_Z19B_Commands.RepeatLastResp);
                bool res = first.Take(4).SequenceEqual(second.Take(4));
                Log(this, $"\tResponses match: {(res ? "yes" : "no")}");
                return res;
            }
            catch (DataValidationException)
            {
                Log(this, "CRC doesn't match");
                return false;
            }
            catch (TimeoutException)
            {
                Log(this, "Timeout");
                return false;
            }
        }
        private async Task<byte[]> ExchageData(byte cmd, params byte[] args)
        {
            if ((args?.Length ?? 0) > MaxRequestArgs) throw new ArgumentOutOfRangeException(nameof(args));
            if (tknSource.IsCancellationRequested) return Array.Empty<byte>();

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
            int read = 0;
            while (read < response.Length)
            {
                read += await Port.ReadAsync(response, read, response.Length - read, tknSource.Token);
            }

            //Check CRC
            if (read < PacketLength) throw new TimeoutException();
            byte crcComp = ComputeCrc(response);
            byte crcResp = response[(int)MH_Z19_Resp_Structure.CRC];
            if (crcComp != crcResp)
            {
                Log(this, $"CRC Error: response = {crcResp:X2}, computed = {crcComp:X2}");
                throw new DataValidationException(null);
            }

            //Cut out the argument to be returned
            return response.Take((int)MH_Z19_Resp_Structure.CRC).Skip((int)MH_Z19_Resp_Structure.ArgumentBegin).ToArray();
        }

        //Public
        public SerialPortStream Port { get; }
        public List<Coordinates> PointsRaw { get; } = new List<Coordinates>(InitialCapacity);
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
                await Task.Delay(3000);
                Port.DiscardInBuffer();
                if (await VerifyConnection()) 
                {
                    Log(this, "Found the device.");
                    if (AutoDisableABC) await SetABC(false);
                    if (AutoPoll) StartPoll();
                    OnConnected?.Invoke(this, new EventArgs());
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
        private async Task SetABC(bool enable)
        {
            byte[] resp = await ExchageData((byte)MH_Z19B_Commands.SetABC, enable ? (byte)0xA0 : (byte)0x00);
            Log(this, $"ABC {(enable ? "enabled" : "disabled")}, resp = {string.Join(' ', resp.Select(x => x.ToString("X2")))}");
            resp = await ExchageData((byte)MH_Z19B_Commands.GetABC);
            Log(this, $"Reported ABC state: {string.Join(' ', resp.Select(x => x.ToString("X2")))}");
        }
        private void Disconnect(bool _throw = false)
        {
            if (IsPolling) StopPoll();
            if (_throw) if (!IsConnected) throw new InvalidOperationException("Already closed.");
            if (!tknSource.IsCancellationRequested) tknSource.Cancel();
            try
            {
                Port.Close();
                Log(this, "Disconnected from the device.");
            }
            catch (Exception ex)
            {
                Log(this, $"Failed to disconnect from the device: {ex}");
            }
            OnDisconnected?.Invoke(this, new EventArgs());
        }
        public void Disconnect()
        {
            Disconnect(true);
        }
        public void StartPoll()
        {
            if (IsPolling) throw new InvalidOperationException("Already polling.");
            if (!IsConnected) throw new InvalidOperationException("Unable to poll the device, the port is closed.");
            
            Log(this, "Starting continuous polling.");
            timPoll.Start();
        }
        public void StopPoll()
        {
            if (!IsPolling) throw new InvalidOperationException("Already idling.");

            Log(this, "Stopping continuous polling.");
            timPoll.Stop();
        }
        public async Task<byte[]> ExecuteCommand(byte cmd, params byte[] args)
        {
            if (!IsConnected) throw new InvalidOperationException("Unable to send a command to an unconnected device.");
            if (IsPolling) throw new InvalidOperationException("Unable to send a custom command during continuous polling.");

            return await ExchageData(cmd, args);
        }
        public async Task ClearPoints()
        {
            await Task.Run(() =>
            {
                lock (timPoll)
                {
                    PointsLimited.Clear();
                    PointsUnlimited.Clear();
                    PointsRaw.Clear();
                }
            });
        }

        public void Dispose()
        {
            if (IsConnected) Disconnect();
            Port.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}