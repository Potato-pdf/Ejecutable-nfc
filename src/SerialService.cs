using System.IO.Ports;

namespace EjecutableNFC;

public class SerialService
{
    private SerialPort? _port;
    private string _buffer = string.Empty;

    /// <summary>Raised on the serial thread when a valid "UID:..." line is received.</summary>
    public event Action<string>? UidReceived;

    /// <summary>Raised on the serial thread for generic log messages from the Arduino.</summary>
    public event Action<string>? LogMessage;

    /// <summary>Raised on the serial thread when the connection is unexpectedly lost.</summary>
    public event Action? ConnectionLost;

    public bool IsConnected => _port?.IsOpen == true;

    public bool Connect(string portName, int baudRate)
    {
        try
        {
            Disconnect();
            _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout  = 500,
                WriteTimeout = 500,
                NewLine      = "\n"
            };
            _port.DataReceived  += OnDataReceived;
            _port.ErrorReceived += OnErrorReceived;
            _port.Open();
            _buffer = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Error al abrir {portName}: {ex.Message}");
            return false;
        }
    }

    public void Disconnect()
    {
        if (_port is null) return;
        try
        {
            _port.DataReceived  -= OnDataReceived;
            _port.ErrorReceived -= OnErrorReceived;
            if (_port.IsOpen) _port.Close();
            _port.Dispose();
        }
        catch { /* best-effort cleanup */ }
        finally
        {
            _port = null;
            _buffer = string.Empty;
        }
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            if (_port is null || !_port.IsOpen) return;
            _buffer += _port.ReadExisting();

            int idx;
            while ((idx = _buffer.IndexOf('\n')) >= 0)
            {
                string line = _buffer[..idx].Replace("\r", "").Trim();
                _buffer = _buffer[(idx + 1)..];

                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("UID:", StringComparison.OrdinalIgnoreCase))
                {
                    string uid = line[4..].Trim();
                    if (!string.IsNullOrEmpty(uid))
                        UidReceived?.Invoke(uid);
                }
                else
                {
                    LogMessage?.Invoke($"[Arduino] {line}");
                }
            }
        }
        catch
        {
            // Read errors during active communication — ignore silently
        }
    }

    private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e)
    {
        Disconnect();
        ConnectionLost?.Invoke();
    }

    public static string[] GetAvailablePorts() => SerialPort.GetPortNames();
}
