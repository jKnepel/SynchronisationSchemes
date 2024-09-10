using jKnepel.ProteusNet.Serializing;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace jKnepel.SynchronisationSchemes.Benchmark
{
    public class BenchmarkController : MonoBehaviour
    {
        private enum CommunicationFlags : byte
        {
            LoadScene,
            UnloadScene,
            StartHost,
            StartClient,
            StopHost,
            StopClient,
            GetBenchmark,
            DirectionalInput,
            SetObjectNumber
        }

        private TcpListener _tcpListener;
        private bool _isRunning;
        
        private Scene _currentScene;
        private SceneData _sceneData;

        private void Start()
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length < 2 || !int.TryParse(args[1], out var port))
            {
                Application.Quit();
                return;
            }
            
            DontDestroyOnLoad(gameObject);
            StartServer(port);
        }
        
        public async void StartServer(int port)
        {
            _tcpListener = new(IPAddress.Any, port);
            _tcpListener.Start();
            _isRunning = true;
            Debug.Log($"TCP Server started on port {port}");

            await AcceptClientsAsync();
        }

        public void StopServer()
        {
            _isRunning = false;
            _tcpListener?.Stop();
            Debug.Log("TCP Server stopped.");
        }
        
        private async Task AcceptClientsAsync()
        {
            while (_isRunning)
            {
                try
                {
                    var client = await _tcpListener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client);
                }
                catch (SocketException e)
                {
                    Debug.Log($"SocketException: {e.Message}");
                }
            }
        }
        
        private async Task HandleClientAsync(TcpClient client)
        {
            Debug.Log("TCP Client connected.");

            using (var stream = client.GetStream())
            using (var memoryStream = new MemoryStream())
            {
                int bytesRead;
                var buffer = new byte[1024];

                while (_isRunning && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    memoryStream.Write(buffer, 0, bytesRead);
                    while (TryProcessMessages(memoryStream)) {}
                }
            }

            Debug.Log("TCP Client disconnected.");
            client.Close();
        }
        
        private bool TryProcessMessages(MemoryStream memoryStream)
        {
            memoryStream.Seek(0, SeekOrigin.Begin);
            var buffer = memoryStream.ToArray();
            
            var index = 0;
            var processedAnyMessage = false;
            while (index < buffer.Length)
            {
                if (index + 2 > buffer.Length) 
                    break;

                var flag = (CommunicationFlags)buffer[index++];
                var length = buffer[index++];

                if (index + length > buffer.Length)
                    break; // message not complete
                
                var message = new byte[length];
                Array.Copy(buffer, index, message, 0, length);
                index += length;

                HandleMessage(flag, message);
                processedAnyMessage = true;
            }
            
            if (index > 0)
            {
                memoryStream.SetLength(0);
                memoryStream.Write(buffer, index, buffer.Length - index);
            }

            return processedAnyMessage;
        }
        
        private async void HandleMessage(CommunicationFlags flag, byte[] message)
        {
            switch (flag)
            {
                case CommunicationFlags.LoadScene:
                {
                    var sceneName = Encoding.UTF8.GetString(message, 0, message.Length);
                    _currentScene = await LoadScene(sceneName);
                    _sceneData = GameObject.FindWithTag("Manager").GetComponent<SceneData>();
                    break;
                }
                case CommunicationFlags.UnloadScene:
                {
                    await UnloadScene(_currentScene);
                    _currentScene = default;
                    _sceneData = null;
                    break;
                }
                case CommunicationFlags.StartHost:
                {
                    if (_sceneData is null) return;
                    if (_sceneData.NetworkManager)
                        _sceneData.NetworkManager.StartHost();
                    _sceneData.Spawner.SpawnObjects();
                    break;
                }
                case CommunicationFlags.StartClient:
                {
                    if (_sceneData is null) return;
                    if (_sceneData.NetworkManager)
                        _sceneData.NetworkManager.StartClient();
                    _sceneData.Spawner.SpawnObjects();
                    break;
                }
                case CommunicationFlags.StopHost:
                {
                    if (_sceneData is null) return;
                    if (_sceneData.NetworkManager)
                        _sceneData.NetworkManager.StopHost();
                    break;
                }
                case CommunicationFlags.StopClient:
                {
                    if (_sceneData is null) return;
                    if (_sceneData.NetworkManager)
                        _sceneData.NetworkManager.StopClient();
                    break;
                }
                case CommunicationFlags.GetBenchmark:
                {
                    
                    break;
                }
                case CommunicationFlags.DirectionalInput:
                {
                    if (_sceneData is null) return;
                    Reader reader = new(message, new() { UseCompression = false });
                    _sceneData.Player.directionalInput = new(reader.ReadSingle(), reader.ReadSingle());
                    break;
                }
                case CommunicationFlags.SetObjectNumber:
                {
                    if (_sceneData is null) return;
                    Reader reader = new(message, new() { UseCompression = false });
                    _sceneData.Spawner.numberOfObjects = reader.ReadInt32();
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static async Task<Scene> LoadScene(string sceneName)
        {
            var asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            while (!asyncLoad.isDone)
                await Task.Yield();

            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.isLoaded)
                throw new("Scene did not correctly load!");
            return scene;
        }

        private static async Task UnloadScene(Scene scene)
        {
            var asyncLoad = SceneManager.UnloadSceneAsync(scene);
            while (!asyncLoad.isDone)
                await Task.Yield();
        }
    }
}
