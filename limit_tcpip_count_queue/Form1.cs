using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace limit_tcpip_count_queue
{    
    public partial class Form1 : Form
    {
        private const int MaxConcurrentConnections = 2;
        private static SemaphoreSlim _connectionSemaphore = new SemaphoreSlim(MaxConcurrentConnections);
        private static ConcurrentQueue<TcpClient> _pendingConnections = new ConcurrentQueue<TcpClient>();
        private TcpListener _listener;
        private CancellationTokenSource _cancellationTokenSource;


        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            StartServer();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            StopServer();
        }

        private void StartServer()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, 12345); // Change port number as needed
            _listener.Start();

            Task.Run(() => AcceptClientsAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
            Log("Server started.");
        }

        private void StopServer()
        {
            _cancellationTokenSource.Cancel();
            _listener?.Stop();
            Log("Server stopped.");
        }

        private async Task AcceptClientsAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync();

                    // 異步地尝试获取信号量的剩餘量是否>0
                    if (await _connectionSemaphore.WaitAsync(0))
                    {
                        // Process the client
                        _ = HandleClientAsync(client);
                    }
                    else
                    {
                        // Queue the client if max connections reached
                        _pendingConnections.Enqueue(client);
                        Log("Connection queueing: Connection limit reached.");

                        //一种启动异步任务的方式，而不关心该任务的结果或完成状态。它适用于需要在后台处理某些操作而不阻塞主线程的情况，如处理排队中的连接。
                        //这种方式确保了 UI 线程保持响应，并允许后台任务异步完成。
                        _ = ProcessPendingConnectionsAsync(); // Attempt to process queued connections
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // Listener stopped
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            try
            {
                using (var networkStream = client.GetStream())
                {
                    var buffer = new byte[1024];
                    var receivedData = new StringBuilder();

                    while (true)
                    {
                        var bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead == 0)
                        {
                            // 客户端断开连接
                            break;
                        }

                        // 将接收到的数据转换为字符串并追加到 receivedData
                        receivedData.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
                        Log($"Received: {receivedData}");
                        Thread.Sleep(5000);

                        // 处理接收到的数据（根据需要修改）
                        // 在此处添加数据处理逻辑
                        // ...

                        // 向客户端发送 "OK" 响应
                        var responseMessage = "OK";
                        var responseBytes = Encoding.ASCII.GetBytes(responseMessage);
                        await networkStream.WriteAsync(responseBytes, 0, responseBytes.Length);

                        // 清除 receivedData 以处理下一个数据块
                        receivedData.Clear();

                        // 暂停处理（如果有必要）
                        //await Task.Delay(5000); // 使用异步延迟代替 Thread.Sleep
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error: {ex.Message}");
            }
            finally
            {
                // 确保在处理完成后释放信号量
                _connectionSemaphore.Release();
            }
        }


        private async Task ProcessPendingConnectionsAsync()
        {
            while (true)
            {
                // 退出循环的条件
                if (_pendingConnections.IsEmpty)
                {
                    break; // 如果队列为空，退出
                }

                // 尝试从队列中取出任务
                if (_pendingConnections.TryDequeue(out var client))
                {
                    // 尝试获取信号量
                    if (await _connectionSemaphore.WaitAsync(0))
                    {
                        // 有空闲资源，处理连接
                        _ = HandleClientAsync(client);

                        // 确保在完成任务后，释放信号量
                        // 注意：HandleClientAsync 方法内部会释放信号量
                    }
                    else
                    {
                        // 如果没有空闲的信号量，将任务重新排队，并等待
                        _pendingConnections.Enqueue(client);

                        // 延迟一段时间再继续处理，以避免过度频繁地处理队列
                        await Task.Delay(100); // 延迟 100 毫秒再继续处理
                    }
                }
                else
                {
                    // 如果无法从队列中取出任务，则退出循环
                    break;
                }
            }
        }


        private void Log(string message)
        {
            // Thread-safe logging to a TextBox or other UI element
            if (InvokeRequired)
            {
                Invoke(new Action(() => Log(message)));
            }
            else
            {
                textBox1.AppendText($"{DateTime.Now}: {message}{Environment.NewLine}");
            }
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }


    }
}
