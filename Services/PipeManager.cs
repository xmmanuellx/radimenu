using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace RadiMenu.Services;

public class PipeManager
{
    private const string PipeName = "RadiMenuPipe";
    private bool _isRunning;

    /// <summary>
    /// Starts a named pipe server to listen for messages from other instances.
    /// </summary>
    public async void StartServer(Action<string> onMessageReceived)
    {
        _isRunning = true;
        
        while (_isRunning)
        {
            try
            {
                // Create pipe with security settings that allow current user access
                // Note: In .NET 8 on Windows, complex security might be needed, 
                // but defaulting to current user scope is usually sufficient for single-user apps.
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                
                await server.WaitForConnectionAsync();
                
                using var reader = new StreamReader(server, Encoding.UTF8);
                var message = await reader.ReadToEndAsync();
                
                if (!string.IsNullOrEmpty(message))
                {
                    // Dispatch to UI thread if needed, or handle here
                    onMessageReceived?.Invoke(message);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PipeServer] Error: {ex.Message}");
                // Slight delay to prevent tight loop on error
                await Task.Delay(1000); 
            }
        }
    }

    /// <summary>
    /// Sends a message to the existing application instance.
    /// Returns true if successful.
    /// </summary>
    public static bool SendMessage(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1000); // Wait 1s max
            
            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.Write(message);
            writer.Flush();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PipeClient] Error: {ex.Message}");
            return false;
        }
    }

    public void StopServer()
    {
        _isRunning = false;
        // Connect a dummy client to unblock WaitForConnection, 
        // or just let it die with the process. 
        // For a simple app closing, usually not strictly necessary to force-close gracefully 
        // if the process is terminating anyway.
    }
}
