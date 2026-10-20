// Test-only responder for validating OrderService's handling of corrupt payment responses.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

public sealed class PaymentFaultServer : IDisposable
{
    private readonly TcpListener listener;
    private readonly object gate = new object();
    private readonly List<TcpClient> clients = new List<TcpClient>();
    private readonly string body;
    private readonly int status;
    private bool stopped;

    public PaymentFaultServer(int port, string body, int status)
    {
        this.body = body;
        this.status = status;
        listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Task.Run((Action)Run);
    }

    private void Run()
    {
        while (true)
        {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); } catch { return; }
            lock (gate)
            {
                if (stopped) { client.Dispose(); return; }
                clients.Add(client);
            }
            Task.Run(() => Respond(client));
        }
    }

    private void Respond(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 3000;
                var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                reader.ReadLine();
                int length = 0;
                string header;
                while (!String.IsNullOrEmpty(header = reader.ReadLine()))
                    if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        length = Int32.Parse(header.Substring(15).Trim());
                while (length-- > 0 && reader.Read() != -1) { }
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                byte[] headers = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " Simulated\r\nContent-Type: application/json\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
                stream.Write(headers, 0, headers.Length);
                stream.Write(bytes, 0, bytes.Length);
            }
            catch { }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            stopped = true;
            listener.Stop();
            foreach (var client in clients) client.Dispose();
        }
    }
}
