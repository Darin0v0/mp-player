using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.IO.Pipes;
using System.Text.Json;

namespace TerminalMusicPlayer
{
    class Program
    {
        // Player state
        static Process? mpvProcess = null;
        static MpvIpcClient? mpvIpcClient = null;
        static List<string> playlist = new List<string>();
        static int currentTrackIndex = 0;
        static bool isRunning = true;
        static bool isPaused = false;
        static string currentFileName = "";
        static float currentVolume = 50f;
        static bool volumeChanged = false;

        // UI state
        static bool showFileExplorer = false;
        static bool showAllTracks = false;
        static bool showThemeSelector = false;
        static bool showVisualizer = true;
        static int selectedIndex = 0;
        static bool needsRedraw = true;
        static string statusMessage = "";
        static DateTime statusMessageTime = DateTime.MinValue;

        // Theme system
        static Theme currentTheme = Theme.Lain;
        static Theme[] availableThemes = { Theme.Lain, Theme.Cyberpunk, Theme.Matrix, Theme.Solarized, Theme.Dracula, Theme.Monokai, Theme.Retro, Theme.Neon };

        // Playback mode
        static bool shuffleMode = false;
        static bool repeatMode = false;
        static List<int> shuffleOrder = new List<int>();
        static int currentShuffleIndex = 0;

        // File system
        static string currentDirectory = "";
        static List<FileSystemEntry> fileSystemEntries = new List<FileSystemEntry>();
        static Stack<string> directoryHistory = new Stack<string>();
        static string[] supportedFormats = { ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".wma", ".aac", ".opus" };
        
        // Pagination for large folders
        static int currentPage = 0;
        static int itemsPerPage = 20;
        static int totalPages = 1;

        // Progress tracking
        static string ipcSocketPath = "";
        static double currentPosition = 0;
        static double totalDuration = 0;
        static bool useMpv = false;

        // Animation state
        static int animationFrame = 0;
        static DateTime lastAnimationTime = DateTime.Now;
        static DateTime lastVisualizerUpdate = DateTime.Now;

        // Frame rate control
        static int targetFps = 30;
        static TimeSpan targetFrameTime = TimeSpan.FromMilliseconds(1000.0 / 30.0);
        static Stopwatch frameTimer = new Stopwatch();
        static Stopwatch fpsTimer = new Stopwatch();
        static int frameCount = 0;
        static double currentFps = 0;

        // Visualizer data
        static float[] audioData = new float[256];
        static float[] spectrumData = new float[32];
        static Random random = new Random();
        static float visualizerIntensity = 1.0f;
        static VisualizerMode visualizerMode = VisualizerMode.Bars;
        static List<Particle> particles = new List<Particle>();
        static List<Star> stars = new List<Star>();
        static List<MatrixChar> matrixChars = new List<MatrixChar>();
        static float bassLevel = 0f;
        static float trebleLevel = 0f;
        static float midLevel = 0f;

        // Visualizer animation state
        static bool visualizerPaused = false;
        static float pauseTimer = 0f;
        static float pauseDecayRate = 0.05f;

        // Screen buffer for flicker-free rendering
        static char[,] currentBuffer;
        static ConsoleColor[,] currentFgBuffer;
        static ConsoleColor[,] currentBgBuffer;
        static char[,] previousBuffer;
        static ConsoleColor[,] previousFgBuffer;
        static ConsoleColor[,] previousBgBuffer;
        static int consoleWidth = 0;
        static int consoleHeight = 0;

        // Theme definitions
        enum Theme
        {
            Lain,
            Cyberpunk,
            Matrix,
            Solarized,
            Dracula,
            Monokai,
            Retro,
            Neon
        }

        enum VisualizerMode
        {
            Bars,
            Wave,
            Particles,
            Spectrum,
            Stars,
            Matrix,
            Equalizer,
            Spiral
        }

        struct ThemeColors
        {
            public ConsoleColor Background;
            public ConsoleColor Text;
            public ConsoleColor Primary;
            public ConsoleColor Secondary;
            public ConsoleColor Accent;
            public ConsoleColor Border;
            public ConsoleColor Highlight;
            public ConsoleColor Progress;
            public ConsoleColor ProgressBg;
            public ConsoleColor Volume;
            public ConsoleColor VolumeBg;
            public ConsoleColor Status;
            public ConsoleColor Glitch;
            public ConsoleColor Warning;
            public ConsoleColor Success;
            public ConsoleColor Visualizer1;
            public ConsoleColor Visualizer2;
            public ConsoleColor Visualizer3;
        }

        struct Particle
        {
            public float X;
            public float Y;
            public float VelocityX;
            public float VelocityY;
            public int Life;
            public ConsoleColor Color;
            public char Character;
        }

        struct Star
        {
            public float X;
            public float Y;
            public float Speed;
            public float Brightness;
        }

        struct MatrixChar
        {
            public float X;
            public float Y;
            public float Speed;
            public char Character;
            public int Life;
            public ConsoleColor Color;
        }

        static ThemeColors GetThemeColors(Theme theme)
        {
            return theme switch
            {
                Theme.Lain => new ThemeColors
                {
                    Background = ConsoleColor.Black,
                    Text = ConsoleColor.Cyan,
                    Primary = ConsoleColor.Magenta,
                    Secondary = ConsoleColor.DarkCyan,
                    Accent = ConsoleColor.DarkMagenta,
                    Border = ConsoleColor.DarkCyan,
                    Highlight = ConsoleColor.White,
                    Progress = ConsoleColor.Magenta,
                    ProgressBg = ConsoleColor.DarkGray,
                    Volume = ConsoleColor.Cyan,
                    VolumeBg = ConsoleColor.DarkGray,
                    Status = ConsoleColor.Green,
                    Glitch = ConsoleColor.Red,
                    Warning = ConsoleColor.Yellow,
                    Success = ConsoleColor.Green,
                    Visualizer1 = ConsoleColor.Magenta,
                    Visualizer2 = ConsoleColor.Cyan,
                    Visualizer3 = ConsoleColor.DarkMagenta
                },
                Theme.Cyberpunk => new ThemeColors
                {
                    Background = ConsoleColor.Black,
                    Text = ConsoleColor.White,
                    Primary = ConsoleColor.Cyan,
                    Secondary = ConsoleColor.DarkCyan,
                    Accent = ConsoleColor.Magenta,
                    Border = ConsoleColor.Blue,
                    Highlight = ConsoleColor.Yellow,
                    Progress = ConsoleColor.Cyan,
                    ProgressBg = ConsoleColor.DarkGray,
                    Volume = ConsoleColor.Blue,
                    VolumeBg = ConsoleColor.DarkGray,
                    Status = ConsoleColor.Green,
                    Glitch = ConsoleColor.Yellow,
                    Warning = ConsoleColor.Yellow,
                    Success = ConsoleColor.Green,
                    Visualizer1 = ConsoleColor.Cyan,
                    Visualizer2 = ConsoleColor.Magenta,
                    Visualizer3 = ConsoleColor.Blue
                },
                Theme.Matrix => new ThemeColors
                {
                    Background = ConsoleColor.Black,
                    Text = ConsoleColor.Green,
                    Primary = ConsoleColor.DarkGreen,
                    Secondary = ConsoleColor.Green,
                    Accent = ConsoleColor.White,
                    Border = ConsoleColor.DarkGreen,
                    Highlight = ConsoleColor.White,
                    Progress = ConsoleColor.Green,
                    ProgressBg = ConsoleColor.DarkGray,
                    Volume = ConsoleColor.DarkGreen,
                    VolumeBg = ConsoleColor.DarkGray,
                    Status = ConsoleColor.White,
                    Glitch = ConsoleColor.Yellow,
                    Warning = ConsoleColor.Yellow,
                    Success = ConsoleColor.White,
                    Visualizer1 = ConsoleColor.Green,
                    Visualizer2 = ConsoleColor.DarkGreen,
                    Visualizer3 = ConsoleColor.White
                },
                Theme.Solarized => new ThemeColors
                {
                    Background = ConsoleColor.DarkBlue,
                    Text = ConsoleColor.Gray,
                    Primary = ConsoleColor.Yellow,
                    Secondary = ConsoleColor.DarkYellow,
                    Accent = ConsoleColor.Cyan,
                    Border = ConsoleColor.DarkCyan,
                    Highlight = ConsoleColor.White,
                    Progress = ConsoleColor.Yellow,
                    ProgressBg = ConsoleColor.DarkGray,
                    Volume = ConsoleColor.Cyan,
                    VolumeBg = ConsoleColor.DarkGray,
                    Status = ConsoleColor.Green,
                    Glitch = ConsoleColor.Red,
                    Warning = ConsoleColor.Yellow,
                    Success = ConsoleColor.Green,
                    Visualizer1 = ConsoleColor.Yellow,
                    Visualizer2 = ConsoleColor.Cyan,
                    Visualizer3 = ConsoleColor.DarkYellow
                },
                Theme.Dracula => new ThemeColors
                {
                    Background = ConsoleColor.DarkMagenta,
                    Text = ConsoleColor.White,
                    Primary = ConsoleColor.Cyan,
                    Secondary = ConsoleColor.Blue,
                    Accent = ConsoleColor.Yellow,
                    Border = ConsoleColor.DarkBlue,
                    Highlight = ConsoleColor.Magenta,
                    Progress = ConsoleColor.Cyan,
                    ProgressBg = ConsoleColor.DarkGray,
                    Volume = ConsoleColor.Blue,
                    VolumeBg = ConsoleColor.DarkGray,
                    Status = ConsoleColor.Green,
                    Glitch = ConsoleColor.Red,
                    Warning = ConsoleColor.Yellow,
                    Success = ConsoleColor.Green,
                    Visualizer1 = ConsoleColor.Cyan,
                    Visualizer2 = ConsoleColor.Magenta,
                    Visualizer3 = ConsoleColor.Yellow
                },
                Theme.Monokai => new ThemeColors
                {
                    Background = ConsoleColor.DarkGray,
                    Text = ConsoleColor.White,
                    Primary = ConsoleColor.Yellow,
                    Secondary = ConsoleColor.Magenta,
                    Accent = ConsoleColor.Green,
                    Border = ConsoleColor.DarkYellow,
                    Highlight = ConsoleColor.Cyan,
                    Progress = ConsoleColor.Green,
                    ProgressBg = ConsoleColor.Black,
                    Volume = ConsoleColor.Magenta,
                    VolumeBg = ConsoleColor.Black,
                    Status = ConsoleColor.Cyan,
                    Glitch = ConsoleColor.Red,
                    Warning = ConsoleColor.Yellow,
                    Success = ConsoleColor.Green,
                    Visualizer1 = ConsoleColor.Yellow,
                    Visualizer2 = ConsoleColor.Magenta,
                    Visualizer3 = ConsoleColor.Green
                },
                Theme.Retro => new ThemeColors
                {
                    Background = ConsoleColor.DarkBlue,
                    Text = ConsoleColor.Yellow,
                    Primary = ConsoleColor.Green,
                    Secondary = ConsoleColor.Cyan,
                    Accent = ConsoleColor.Red,
                    Border = ConsoleColor.DarkYellow,
                    Highlight = ConsoleColor.White,
                    Progress = ConsoleColor.Green,
                    ProgressBg = ConsoleColor.DarkGray,
                    Volume = ConsoleColor.Cyan,
                    VolumeBg = ConsoleColor.DarkGray,
                    Status = ConsoleColor.Red,
                    Glitch = ConsoleColor.Magenta,
                    Warning = ConsoleColor.Yellow,
                    Success = ConsoleColor.Green,
                    Visualizer1 = ConsoleColor.Green,
                    Visualizer2 = ConsoleColor.Red,
                    Visualizer3 = ConsoleColor.Cyan
                },
                Theme.Neon => new ThemeColors
                {
                    Background = ConsoleColor.Black,
                    Text = ConsoleColor.White,
                    Primary = ConsoleColor.Magenta,
                    Secondary = ConsoleColor.Cyan,
                    Accent = ConsoleColor.Yellow,
                    Border = ConsoleColor.DarkMagenta,
                    Highlight = ConsoleColor.White,
                    Progress = ConsoleColor.Magenta,
                    ProgressBg = ConsoleColor.DarkGray,
                    Volume = ConsoleColor.Cyan,
                    VolumeBg = ConsoleColor.DarkGray,
                    Status = ConsoleColor.Yellow,
                    Glitch = ConsoleColor.Cyan,
                    Warning = ConsoleColor.Yellow,
                    Success = ConsoleColor.Green,
                    Visualizer1 = ConsoleColor.Magenta,
                    Visualizer2 = ConsoleColor.Cyan,
                    Visualizer3 = ConsoleColor.Yellow
                },
                _ => GetThemeColors(Theme.Lain)
            };
        }

        // Simple ASCII logos
        static readonly string[] lainLogo = {
            "╔══════════════════════════════╗",
            "║   SERIAL EXPERIMENTS LAIN   ║",
            "╚══════════════════════════════╝"
        };

        static readonly string[] cyberpunkLogo = {
            "╔══════════════════════════════╗",
            "║     CYBERPUNK  PLAYER       ║",
            "╚══════════════════════════════╝"
        };

        static readonly string[] matrixLogo = {
            "╔══════════════════════════════╗",
            "║       THE  MATRIX           ║",
            "╚══════════════════════════════╝"
        };

        static readonly string[] retroLogo = {
            "╔══════════════════════════════╗",
            "║        RETRO  PLAYER        ║",
            "╚══════════════════════════════╝"
        };

        static readonly string[] neonLogo = {
            "╔══════════════════════════════╗",
            "║        NEON  PLAYER         ║",
            "╚══════════════════════════════╝"
        };

        // IPC Client for MPV
        class MpvIpcClient : IDisposable
        {
            private string _socketPath;
            private Stream? _stream;
            private Thread? _readThread;
            private bool _isDisposed = false;
            private Socket? _socket;
            private NamedPipeClientStream? _pipeClient;

            public MpvIpcClient(string socketPath)
            {
                _socketPath = socketPath;
            }

            public bool IsConnected => _stream != null && _stream.CanWrite;

            public void Connect()
            {
                try
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        _pipeClient = new NamedPipeClientStream(".", _socketPath, PipeDirection.InOut);
                        _pipeClient.Connect(3000);
                        _stream = _pipeClient;
                    }
                    else
                    {
                        _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
                        var endPoint = new UnixDomainSocketEndPoint(_socketPath);
                        _socket.Connect(endPoint);
                        _stream = new NetworkStream(_socket, true);
                    }

                    _readThread = new Thread(ReadMessages);
                    _readThread.IsBackground = true;
                    _readThread.Start();

                    SendCommand(new { command = new object[] { "observe_property", 1, "pause" } });
                    SendCommand(new { command = new object[] { "observe_property", 2, "time-pos" } });
                    SendCommand(new { command = new object[] { "observe_property", 3, "duration" } });
                    SendCommand(new { command = new object[] { "observe_property", 4, "volume" } });
                    
                    Thread.Sleep(100);
                    SendCommand(new { command = new object[] { "get_property", "volume" } });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to connect to MPV: {ex.Message}");
                }
            }

            public void SendCommand(object command)
            {
                try
                {
                    if (_stream != null && _stream.CanWrite)
                    {
                        string json = JsonSerializer.Serialize(command, new JsonSerializerOptions 
                        { 
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                            WriteIndented = false
                        });
                        byte[] buffer = Encoding.UTF8.GetBytes(json + "\n");
                        _stream.Write(buffer, 0, buffer.Length);
                        _stream.Flush();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error sending command: {ex.Message}");
                }
            }

            private void ReadMessages()
            {
                byte[] buffer = new byte[4096];
                StringBuilder messageBuffer = new StringBuilder();
                
                while (!_isDisposed && _stream != null)
                {
                    try
                    {
                        int bytesRead = _stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            messageBuffer.Append(chunk);
                            
                            string[] messages = messageBuffer.ToString().Split('\n');
                            for (int i = 0; i < messages.Length - 1; i++)
                            {
                                if (!string.IsNullOrWhiteSpace(messages[i]))
                                {
                                    ProcessMessage(messages[i].Trim());
                                }
                            }
                            
                            messageBuffer.Clear();
                            if (messages.Length > 0 && !string.IsNullOrWhiteSpace(messages[messages.Length - 1]))
                            {
                                messageBuffer.Append(messages[messages.Length - 1]);
                            }
                        }
                        else
                        {
                            Thread.Sleep(10);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!_isDisposed)
                            Debug.WriteLine($"Error reading from MPV: {ex.Message}");
                        break;
                    }
                }
            }

            private void ProcessMessage(string message)
            {
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(message);
                    JsonElement root = doc.RootElement;

                    if (root.TryGetProperty("event", out JsonElement eventElement))
                    {
                        string eventType = eventElement.GetString() ?? "";
                        
                        if (eventType == "property-change" && root.TryGetProperty("name", out JsonElement nameElement))
                        {
                            string propertyName = nameElement.GetString() ?? "";
                            
                            if (root.TryGetProperty("data", out JsonElement dataElement))
                            {
                                switch (propertyName)
                                {
                                    case "pause":
                                        if (dataElement.ValueKind == JsonValueKind.True || dataElement.ValueKind == JsonValueKind.False)
                                        {
                                            bool wasPaused = isPaused;
                                            isPaused = dataElement.GetBoolean();
                                            
                                            // Update visualizer state when pause state changes
                                            if (wasPaused != isPaused)
                                            {
                                                visualizerPaused = isPaused;
                                                if (isPaused)
                                                {
                                                    // Start decay when pausing
                                                    pauseTimer = 1.0f;
                                                }
                                                else
                                                {
                                                    // Resume animation when playing
                                                    pauseTimer = 0f;
                                                }
                                                needsRedraw = true;
                                            }
                                        }
                                        break;
                                    case "time-pos":
                                        if (dataElement.ValueKind == JsonValueKind.Number)
                                        {
                                            currentPosition = dataElement.GetDouble();
                                            needsRedraw = true;
                                        }
                                        else if (dataElement.ValueKind == JsonValueKind.Null)
                                        {
                                            currentPosition = 0;
                                            needsRedraw = true;
                                        }
                                        break;
                                    case "duration":
                                        if (dataElement.ValueKind == JsonValueKind.Number)
                                        {
                                            totalDuration = dataElement.GetDouble();
                                            needsRedraw = true;
                                        }
                                        break;
                                    case "volume":
                                        if (dataElement.ValueKind == JsonValueKind.Number)
                                        {
                                            currentVolume = dataElement.GetSingle();
                                            needsRedraw = true;
                                        }
                                        break;
                                }
                            }
                        }
                        else if (eventType == "end-file")
                        {
                            if (!isPaused && playlist.Count > 0)
                            {
                                if (repeatMode)
                                {
                                    PlayCurrentTrack();
                                }
                                else
                                {
                                    NextTrack();
                                }
                            }
                        }
                    }
                    else if (root.TryGetProperty("error", out JsonElement errorElement) && 
                             errorElement.ValueKind == JsonValueKind.String &&
                             errorElement.GetString() == "success")
                    {
                        if (root.TryGetProperty("data", out JsonElement dataElement) && 
                            dataElement.ValueKind == JsonValueKind.Number)
                        {
                            float volumeValue = dataElement.GetSingle();
                            if (volumeValue >= 0 && volumeValue <= 100)
                            {
                                currentVolume = volumeValue;
                                needsRedraw = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing MPV message: {ex.Message}");
                }
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected virtual void Dispose(bool disposing)
            {
                if (!_isDisposed)
                {
                    if (disposing)
                    {
                        try
                        {
                            _readThread?.Join(100);
                        }
                        catch { }

                        try
                        {
                            _stream?.Close();
                            _stream?.Dispose();
                        }
                        catch { }

                        try
                        {
                            _pipeClient?.Close();
                            _pipeClient?.Dispose();
                        }
                        catch { }

                        try
                        {
                            _socket?.Close();
                            _socket?.Dispose();
                        }
                        catch { }

                        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            try 
                            { 
                                if (File.Exists(_socketPath))
                                    File.Delete(_socketPath); 
                            } 
                            catch { }
                        }
                    }

                    _isDisposed = true;
                }
            }

            ~MpvIpcClient()
            {
                Dispose(false);
            }
        }

        class FileSystemEntry
        {
            public string Name { get; set; } = "";
            public string FullPath { get; set; } = "";
            public bool IsDirectory { get; set; }
            public bool IsParentDirectory { get; set; }
        }

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
            Console.CursorVisible = false;
            Console.Title = "TERMINAL MUSIC PLAYER";
            Console.TreatControlCAsInput = true;

            // Initialize visualizer data
            InitializeVisualizer();

            // Initialize frame rate control
            frameTimer.Start();
            fpsTimer.Start();

            try
            {
                useMpv = IsMpvAvailable();
                if (!useMpv)
                {
                    DrawErrorScreen();
                    return;
                }

                InitializeConsole();
                SetInitialDirectory();

                if (args.Length > 0)
                {
                    LoadFilesFromArgs(args);
                }

                if (playlist.Count == 0)
                {
                    ShowFileExplorer();
                }
                else
                {
                    PlayCurrentTrack();
                }

                // Main game loop with frame rate control
                while (isRunning)
                {
                    TimeSpan frameStart = frameTimer.Elapsed;

                    // Handle input
                    if (Console.KeyAvailable)
                    {
                        HandleInput();
                    }

                    // Update game state
                    Update();

                    // Render frame
                    Render();

                    // Frame rate control
                    TimeSpan frameTime = frameTimer.Elapsed - frameStart;
                    TimeSpan sleepTime = targetFrameTime - frameTime;
                    
                    if (sleepTime > TimeSpan.Zero)
                    {
                        Thread.Sleep(sleepTime);
                    }

                    // Calculate FPS
                    frameCount++;
                    if (fpsTimer.Elapsed.TotalSeconds >= 1.0)
                    {
                        currentFps = frameCount / fpsTimer.Elapsed.TotalSeconds;
                        frameCount = 0;
                        fpsTimer.Restart();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Clear();
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
            finally
            {
                CleanUp();
            }
        }

        static void InitializeVisualizer()
        {
            // Initialize audio data with random values
            for (int i = 0; i < audioData.Length; i++)
            {
                audioData[i] = (float)(random.NextDouble() * 0.3);
            }

            // Initialize stars for starfield visualizer
            for (int i = 0; i < 100; i++)
            {
                stars.Add(new Star
                {
                    X = (float)random.NextDouble() * consoleWidth,
                    Y = (float)random.NextDouble() * consoleHeight,
                    Speed = 0.1f + (float)random.NextDouble() * 0.5f,
                    Brightness = (float)random.NextDouble()
                });
            }

            // Initialize matrix characters
            for (int i = 0; i < 200; i++)
            {
                matrixChars.Add(new MatrixChar
                {
                    X = (float)random.NextDouble() * consoleWidth,
                    Y = (float)random.NextDouble() * consoleHeight,
                    Speed = 1f + (float)random.NextDouble() * 3f,
                    Character = GetRandomMatrixChar(),
                    Life = random.Next(10, 50),
                    Color = ConsoleColor.Green
                });
            }
        }

        static void UpdateVisualizer()
        {
            if ((DateTime.Now - lastVisualizerUpdate).TotalMilliseconds < 16) return;
            
            lastVisualizerUpdate = DateTime.Now;

            // If music is paused, decay the visualizer
            if (isPaused)
            {
                if (pauseTimer > 0)
                {
                    pauseTimer -= pauseDecayRate;
                    if (pauseTimer < 0) pauseTimer = 0;
                    
                    // Apply decay to all audio data
                    for (int i = 0; i < audioData.Length; i++)
                    {
                        audioData[i] *= 0.9f; // Fast decay when pausing
                    }
                    
                    for (int i = 0; i < spectrumData.Length; i++)
                    {
                        spectrumData[i] *= 0.9f;
                    }
                    
                    bassLevel *= 0.9f;
                    midLevel *= 0.9f;
                    trebleLevel *= 0.9f;
                }
                else
                {
                    // When fully paused, set minimal values
                    for (int i = 0; i < audioData.Length; i++)
                    {
                        audioData[i] = 0f;
                    }
                    
                    for (int i = 0; i < spectrumData.Length; i++)
                    {
                        spectrumData[i] = 0f;
                    }
                    
                    bassLevel = 0f;
                    midLevel = 0f;
                    trebleLevel = 0f;
                }
                
                // Still update animations but with minimal movement
                animationFrame = (animationFrame + 1) % 4;
                UpdateParticlesMinimal();
                UpdateStars();
                UpdateMatrixChars();
                return;
            }

            // Music is playing - generate realistic audio data
            float time = (float)DateTime.Now.TimeOfDay.TotalSeconds * 2f;
            
            // Bass frequencies (0-100Hz) - slow, powerful waves
            float bass = (float)(Math.Sin(time * 0.3) * 0.4 + 
                                 Math.Sin(time * 0.7) * 0.3 + 
                                 Math.Sin(time * 1.2) * 0.2);
            bassLevel = Math.Clamp(Math.Abs(bass) + (float)random.NextDouble() * 0.1f, 0, 1);
            
            // Mid frequencies (100-4000Hz) - more complex patterns
            float mid = (float)(Math.Sin(time * 2.0) * 0.3 + 
                                Math.Sin(time * 3.5) * 0.25 +
                                Math.Sin(time * 5.2) * 0.15);
            midLevel = Math.Clamp(Math.Abs(mid) + (float)random.NextDouble() * 0.08f, 0, 1);
            
            // Treble frequencies (4000-20000Hz) - fast, detailed patterns
            float treble = (float)(Math.Sin(time * 8.0) * 0.25 + 
                                   Math.Sin(time * 12.5) * 0.2 +
                                   Math.Sin(time * 18.3) * 0.15);
            trebleLevel = Math.Clamp(Math.Abs(treble) + (float)random.NextDouble() * 0.06f, 0, 1);

            // Update audio data with frequency-based distribution
            for (int i = 0; i < audioData.Length; i++)
            {
                float frequency = (float)i / audioData.Length;
                float value = 0f;

                // Bass region (0-10% of spectrum)
                if (frequency < 0.1f)
                {
                    float bassFactor = 1f - (frequency / 0.1f);
                    value = bassLevel * bassFactor * 1.2f;
                }
                // Low-mid region (10-30%)
                else if (frequency < 0.3f)
                {
                    float midFactor = 1f - Math.Abs((frequency - 0.2f) / 0.1f);
                    value = midLevel * midFactor * 1.1f;
                }
                // High-mid region (30-60%)
                else if (frequency < 0.6f)
                {
                    float midFactor = 1f - Math.Abs((frequency - 0.45f) / 0.15f);
                    value = midLevel * midFactor * 0.9f;
                }
                // Treble region (60-100%)
                else
                {
                    float trebleFactor = 1f - ((frequency - 0.6f) / 0.4f);
                    value = trebleLevel * trebleFactor * 0.8f;
                }

                // Smooth transitions and add some noise
                float smoothness = 0.7f;
                float change = (float)(random.NextDouble() - 0.5) * 0.05f;
                audioData[i] = audioData[i] * smoothness + (1f - smoothness) * Math.Clamp(value + change, 0, 1);
            }

            // Update spectrum data with better frequency grouping
            for (int i = 0; i < spectrumData.Length; i++)
            {
                // Non-linear frequency distribution (more detail in bass/mid)
                float freqPosition = (float)i / spectrumData.Length;
                float weightedPosition = freqPosition * freqPosition; // Quadratic distribution
                
                int start = (int)(weightedPosition * audioData.Length);
                int end = (int)(((float)(i + 1) / spectrumData.Length) * ((float)(i + 1) / spectrumData.Length) * audioData.Length);
                
                if (start == end) end = start + 1;
                if (end > audioData.Length) end = audioData.Length;
                
                float sum = 0;
                int count = 0;
                for (int j = start; j < end && j < audioData.Length; j++)
                {
                    sum += audioData[j];
                    count++;
                }
                
                if (count > 0)
                {
                    float avg = sum / count;
                    // Apply frequency-specific intensity
                    if (i < spectrumData.Length / 3) // Bass region
                        avg *= 1.3f;
                    else if (i < spectrumData.Length * 2 / 3) // Mid region
                        avg *= 1.1f;
                    
                    spectrumData[i] = avg * visualizerIntensity;
                }
            }

            // Update particles based on audio intensity
            UpdateParticles();

            // Update stars for starfield
            if (visualizerMode == VisualizerMode.Stars)
            {
                UpdateStars();
            }

            // Update matrix characters
            if (visualizerMode == VisualizerMode.Matrix)
            {
                UpdateMatrixChars();
            }
            
            // Update animation frame
            animationFrame = (animationFrame + 1) % 4;
        }

        static void UpdateParticles()
        {
            // Add new particles based on audio intensity
            if (particles.Count < 200 && random.NextDouble() > 0.5)
            {
                float intensity = audioData[random.Next(audioData.Length)];
                if (intensity > 0.2)
                {
                    particles.Add(new Particle
                    {
                        X = random.Next(consoleWidth),
                        Y = consoleHeight - 1,
                        VelocityX = (float)(random.NextDouble() - 0.5) * 2f,
                        VelocityY = -(float)(random.NextDouble() * 3 + 1),
                        Life = random.Next(30, 90),
                        Color = GetRandomVisualizerColor(),
                        Character = GetParticleChar()
                    });
                }
            }

            // Update existing particles
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var particle = particles[i];
                particle.X += particle.VelocityX;
                particle.Y += particle.VelocityY;
                particle.VelocityY += 0.05f; // gravity
                particle.Life--;

                if (particle.Life <= 0 || particle.Y >= consoleHeight || particle.X < 0 || particle.X >= consoleWidth)
                {
                    particles.RemoveAt(i);
                }
                else
                {
                    particles[i] = particle;
                }
            }
        }

        static void UpdateParticlesMinimal()
        {
            // Minimal particle update when paused - just let existing particles fall
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var particle = particles[i];
                particle.X += particle.VelocityX * 0.1f; // Slow down
                particle.Y += particle.VelocityY * 0.1f;
                particle.VelocityY += 0.02f; // Reduced gravity
                particle.Life--;

                if (particle.Life <= 0 || particle.Y >= consoleHeight || particle.X < 0 || particle.X >= consoleWidth)
                {
                    particles.RemoveAt(i);
                }
                else
                {
                    particles[i] = particle;
                }
            }
        }

        static void UpdateStars()
        {
            float speedMultiplier = isPaused ? 0.1f : 1.0f; // Slow down stars when paused
            
            for (int i = 0; i < stars.Count; i++)
            {
                var star = stars[i];
                star.Y += star.Speed * speedMultiplier;
                if (star.Y >= consoleHeight)
                {
                    star.Y = 0;
                    star.X = (float)random.NextDouble() * consoleWidth;
                    star.Brightness = (float)random.NextDouble();
                }
                stars[i] = star;
            }
        }

        static void UpdateMatrixChars()
        {
            float speedMultiplier = isPaused ? 0.1f : 1.0f; // Slow down matrix when paused
            
            for (int i = matrixChars.Count - 1; i >= 0; i--)
            {
                var mc = matrixChars[i];
                mc.Y += mc.Speed * speedMultiplier;
                mc.Life--;

                if (mc.Life <= 0 || mc.Y >= consoleHeight)
                {
                    mc.Y = 0;
                    mc.X = (float)random.NextDouble() * consoleWidth;
                    mc.Life = random.Next(10, 50);
                    mc.Character = GetRandomMatrixChar();
                }
                matrixChars[i] = mc;
            }

            // Add new matrix characters occasionally (less frequently when paused)
            if (matrixChars.Count < 300 && random.NextDouble() > (isPaused ? 0.9 : 0.7))
            {
                matrixChars.Add(new MatrixChar
                {
                    X = (float)random.NextDouble() * consoleWidth,
                    Y = 0,
                    Speed = 1f + (float)random.NextDouble() * 3f,
                    Character = GetRandomMatrixChar(),
                    Life = random.Next(10, 50),
                    Color = ConsoleColor.Green
                });
            }
        }

        static char GetRandomMatrixChar()
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789$#@%&*";
            return chars[random.Next(chars.Length)];
        }

        static char GetParticleChar()
        {
            char[] chars = { '•', '·', '°', '∗', '⋅', '∘', '∙', '♥', '♠', '♦', '♣' };
            return chars[random.Next(chars.Length)];
        }

        static ConsoleColor GetRandomVisualizerColor()
        {
            var theme = GetThemeColors(currentTheme);
            ConsoleColor[] colors = { theme.Visualizer1, theme.Visualizer2, theme.Visualizer3 };
            return colors[random.Next(colors.Length)];
        }

        static void InitializeConsole()
        {
            consoleWidth = Console.WindowWidth;
            consoleHeight = Console.WindowHeight;
            
            // Initialize double buffers
            currentBuffer = new char[consoleHeight, consoleWidth];
            currentFgBuffer = new ConsoleColor[consoleHeight, consoleWidth];
            currentBgBuffer = new ConsoleColor[consoleHeight, consoleWidth];
            previousBuffer = new char[consoleHeight, consoleWidth];
            previousFgBuffer = new ConsoleColor[consoleHeight, consoleWidth];
            previousBgBuffer = new ConsoleColor[consoleHeight, consoleWidth];

            var theme = GetThemeColors(currentTheme);
            
            // Clear and fill buffers
            for (int y = 0; y < consoleHeight; y++)
            {
                for (int x = 0; x < consoleWidth; x++)
                {
                    currentBuffer[y, x] = ' ';
                    currentFgBuffer[y, x] = theme.Text;
                    currentBgBuffer[y, x] = theme.Background;
                    previousBuffer[y, x] = ' ';
                    previousFgBuffer[y, x] = theme.Text;
                    previousBgBuffer[y, x] = theme.Background;
                }
            }
            
            Console.Clear();
            Console.BackgroundColor = theme.Background;
            Console.ForegroundColor = theme.Text;
        }

        static void ResizeBuffers()
        {
            int newWidth = Console.WindowWidth;
            int newHeight = Console.WindowHeight;

            if (newWidth != consoleWidth || newHeight != consoleHeight)
            {
                consoleWidth = newWidth;
                consoleHeight = newHeight;

                currentBuffer = new char[consoleHeight, consoleWidth];
                currentFgBuffer = new ConsoleColor[consoleHeight, consoleWidth];
                currentBgBuffer = new ConsoleColor[consoleHeight, consoleWidth];
                previousBuffer = new char[consoleHeight, consoleWidth];
                previousFgBuffer = new ConsoleColor[consoleHeight, consoleWidth];
                previousBgBuffer = new ConsoleColor[consoleHeight, consoleWidth];

                var theme = GetThemeColors(currentTheme);
                
                for (int y = 0; y < consoleHeight; y++)
                {
                    for (int x = 0; x < consoleWidth; x++)
                    {
                        currentBuffer[y, x] = ' ';
                        currentFgBuffer[y, x] = theme.Text;
                        currentBgBuffer[y, x] = theme.Background;
                        previousBuffer[y, x] = ' ';
                        previousFgBuffer[y, x] = theme.Text;
                        previousBgBuffer[y, x] = theme.Background;
                    }
                }

                Console.Clear();
                needsRedraw = true;
            }
        }

        static void Update()
        {
            // Check if console size changed
            if (Console.WindowWidth != consoleWidth || Console.WindowHeight != consoleHeight)
            {
                ResizeBuffers();
            }

            // Update visualizer
            if (showVisualizer && !showFileExplorer && !showAllTracks && !showThemeSelector)
            {
                UpdateVisualizer();
            }

            // Clear status message after 3 seconds
            if (!string.IsNullOrEmpty(statusMessage) && (DateTime.Now - statusMessageTime).TotalSeconds > 3)
            {
                statusMessage = "";
                needsRedraw = true;
            }

            // Apply volume changes if any
            if (volumeChanged && mpvIpcClient != null && mpvIpcClient.IsConnected)
            {
                ApplyVolumeToMpv();
                volumeChanged = false;
            }

            // Animation updates (only if not paused)
            if (!isPaused && (DateTime.Now - lastAnimationTime).TotalMilliseconds > 100)
            {
                animationFrame = (animationFrame + 1) % 4;
                lastAnimationTime = DateTime.Now;
                needsRedraw = true;
            }
        }

        static void Render()
        {
            if (!needsRedraw && !showVisualizer) return;

            var theme = GetThemeColors(currentTheme);

            // Clear current buffer
            for (int y = 0; y < consoleHeight; y++)
            {
                for (int x = 0; x < consoleWidth; x++)
                {
                    currentBuffer[y, x] = ' ';
                    currentFgBuffer[y, x] = theme.Text;
                    currentBgBuffer[y, x] = theme.Background;
                }
            }

            // Draw to buffer
            if (showThemeSelector)
                DrawThemeSelectorToBuffer();
            else if (showFileExplorer)
                DrawFileExplorerToBuffer();
            else if (showAllTracks)
                DrawAllTracksToBuffer();
            else
                DrawPlayerToBuffer();

            // Render only changed cells
            for (int y = 0; y < consoleHeight; y++)
            {
                for (int x = 0; x < consoleWidth; x++)
                {
                    if (currentBuffer[y, x] != previousBuffer[y, x] ||
                        currentFgBuffer[y, x] != previousFgBuffer[y, x] ||
                        currentBgBuffer[y, x] != previousBgBuffer[y, x])
                    {
                        Console.SetCursorPosition(x, y);
                        Console.ForegroundColor = currentFgBuffer[y, x];
                        Console.BackgroundColor = currentBgBuffer[y, x];
                        Console.Write(currentBuffer[y, x]);

                        previousBuffer[y, x] = currentBuffer[y, x];
                        previousFgBuffer[y, x] = currentFgBuffer[y, x];
                        previousBgBuffer[y, x] = currentBgBuffer[y, x];
                    }
                }
            }

            needsRedraw = false;
        }

        static void ApplyVolumeToMpv()
        {
            if (mpvIpcClient == null || !mpvIpcClient.IsConnected) return;

            try
            {
                mpvIpcClient.SendCommand(new { command = new object[] { "set_property", "volume", currentVolume } });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting volume: {ex.Message}");
            }
        }

        static void DrawErrorScreen()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n\n    MPV NOT FOUND");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("\n    This player requires MPV to be installed.");
            Console.WriteLine("\n    Installation instructions:");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("    - Windows: winget install mpv");
            Console.WriteLine("    - macOS:   brew install mpv");
            Console.WriteLine("    - Linux:   sudo apt install mpv");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("\n    Press any key to exit...");
            Console.ReadKey();
        }

        static void DrawPlayerToBuffer()
        {
            var theme = GetThemeColors(currentTheme);
            int width = consoleWidth;
            int height = consoleHeight;

            // Draw border
            DrawBorderToBuffer(0, 0, width, height, theme.Border, theme);

            // Draw header
            DrawHeaderToBuffer(width, theme);

            // Visualizer section - FULL WIDTH
            if (showVisualizer)
            {
                int visualizerHeight = Math.Min(16, height / 3);
                DrawVisualizerToBuffer(2, 4, width - 4, visualizerHeight, theme);
            }

            // Now Playing section
            int nowPlayingY = showVisualizer ? 21 : 6;
            if (nowPlayingY + 10 >= height) nowPlayingY = 6;

            DrawBoxToBuffer(2, nowPlayingY, width - 4, 6, "NOW PLAYING", theme.Primary, theme);

            // Track name
            if (!string.IsNullOrEmpty(currentFileName))
            {
                string displayName = Path.GetFileNameWithoutExtension(currentFileName);
                int maxNameLength = width - 10;
                if (displayName.Length > maxNameLength)
                    displayName = displayName.Substring(0, maxNameLength - 3) + "...";
                
                WriteToBuffer(4, nowPlayingY + 2, "♪ " + displayName, theme.Text, theme.Background);
            }
            else
            {
                WriteToBuffer(4, nowPlayingY + 2, "♪ NO TRACK SELECTED", theme.Text, theme.Background);
            }

            // Status
            WriteToBuffer(4, nowPlayingY + 3, "Status: ", theme.Secondary, theme.Background);
            string statusText = isPaused ? "⏸ PAUSED" : "▶ PLAYING";
            ConsoleColor statusColor = isPaused ? theme.Warning : theme.Success;
            WriteToBuffer(12, nowPlayingY + 3, statusText, statusColor, theme.Background);

            // Visualizer state indicator
            if (isPaused)
            {
                WriteToBuffer(width - 20, nowPlayingY + 3, "⏸ VISUALIZER PAUSED", theme.Warning, theme.Background);
            }

            // Mode indicators
            string shuffleIcon = shuffleMode ? "🔀" : "▶";
            string repeatIcon = repeatMode ? "🔁" : "▶";
            WriteToBuffer(width - 24, nowPlayingY + 2, shuffleIcon + " SHUFFLE", shuffleMode ? theme.Accent : theme.Border, theme.Background);
            WriteToBuffer(width - 12, nowPlayingY + 2, repeatIcon + " REPEAT", repeatMode ? theme.Accent : theme.Border, theme.Background);

            // Progress section
            int progressY = nowPlayingY + 6;
            if (progressY + 6 >= height) progressY = nowPlayingY + 4;

            DrawBoxToBuffer(2, progressY, width - 4, 4, "PROGRESS", theme.Secondary, theme);
            
            string timeDisplay = totalDuration > 0 ? 
                $"{FormatTime(currentPosition)} / {FormatTime(totalDuration)}" : 
                $"{FormatTime(currentPosition)} / --:--";
            WriteToBuffer(4, progressY + 2, timeDisplay, theme.Text, theme.Background);

            // Progress bar
            DrawProgressBarToBuffer(4, progressY + 3, width - 8, theme);

            // Volume section
            int volumeY = progressY + 6;
            if (volumeY + 6 >= height) volumeY = progressY + 4;

            DrawBoxToBuffer(2, volumeY, width - 4, 4, "VOLUME", theme.Accent, theme);
            
            WriteToBuffer(4, volumeY + 2, $"🔊 Level: {currentVolume:0}%", theme.Text, theme.Background);
            DrawVolumeBarToBuffer(4, volumeY + 3, Math.Min(40, width - 12), theme);

            // Track info
            WriteToBuffer(4, volumeY + 5, $"Track: {currentTrackIndex + 1} of {playlist.Count}", theme.Text, theme.Background);

            // Controls section
            int controlsY = volumeY + 7;
            int controlsHeight = height - controlsY - 3;
            if (controlsHeight > 4)
            {
                DrawBoxToBuffer(2, controlsY, width - 4, controlsHeight, "CONTROLS", theme.Highlight, theme);

                // Control labels
                int controlTextY = controlsY + 2;
                string[][] controls = {
                    new[] { "SPACE", "Play/Pause" },
                    new[] { "→", "Next Track" },
                    new[] { "←", "Previous Track" },
                    new[] { "↑/↓", "Volume" },
                    new[] { "+/-", "Visualizer Intensity" },
                    new[] { "F", $"Shuffle ({(shuffleMode ? "ON" : "OFF")})" },
                    new[] { "R", $"Repeat ({(repeatMode ? "ON" : "OFF")})" },
                    new[] { "V", $"Visualizer ({(showVisualizer ? "ON" : "OFF")})" },
                    new[] { "M", "Visualizer Mode" },
                    new[] { "E", "File Explorer" },
                    new[] { "A", "All Tracks" },
                    new[] { "T", "Themes" },
                    new[] { "Q", "Quit" }
                };

                for (int i = 0; i < controls.Length; i++)
                {
                    if (controlTextY + i >= height - 2) break;
                    
                    WriteToBuffer(4, controlTextY + i, $"[{controls[i][0]}]", theme.Highlight, theme.Background);
                    WriteToBuffer(4 + controls[i][0].Length + 3, controlTextY + i, $"{controls[i][1]}", theme.Text, theme.Background);
                }
            }

            // Footer with FPS info
            string visualizerState = isPaused ? "PAUSED" : $"{visualizerMode}";
            string footerText = $"│ THEME: {currentTheme} │ VISUALIZER: {visualizerState} │ INTENSITY: {visualizerIntensity:0.0}x │ FPS: {currentFps:0.0} │";
            WriteToBuffer(width / 2 - footerText.Length / 2, height - 2, footerText, theme.Border, theme.Background);

            // Status message
            if (!string.IsNullOrEmpty(statusMessage))
            {
                string statusLine = $"║ {statusMessage} ║";
                WriteToBuffer(2, height - 4, statusLine.PadRight(width - 4), theme.Status, theme.Background);
            }
        }

        static void DrawVisualizerToBuffer(int x, int y, int width, int height, ThemeColors theme)
        {
            string visualizerTitle = isPaused ? $"VISUALIZER - {visualizerMode} - PAUSED" : $"VISUALIZER - {visualizerMode}";
            DrawBoxToBuffer(x, y, width, height, visualizerTitle, theme.Visualizer1, theme);

            int visX = x + 2;
            int visY = y + 2;
            int visWidth = width - 4;
            int visHeight = height - 4;

            // If paused and no activity, show paused message
            if (isPaused && pauseTimer <= 0 && spectrumData.All(s => s < 0.01f))
            {
                string pausedMsg = "⏸ VISUALIZER PAUSED";
                WriteToBuffer(x + width / 2 - pausedMsg.Length / 2, y + height / 2, pausedMsg, theme.Warning, theme.Background);
                return;
            }

            switch (visualizerMode)
            {
                case VisualizerMode.Bars:
                    DrawBarVisualizer(visX, visY, visWidth, visHeight, theme);
                    break;
                case VisualizerMode.Wave:
                    DrawWaveVisualizer(visX, visY, visWidth, visHeight, theme);
                    break;
                case VisualizerMode.Particles:
                    DrawParticleVisualizer(visX, visY, visWidth, visHeight, theme);
                    break;
                case VisualizerMode.Spectrum:
                    DrawSpectrumVisualizer(visX, visY, visWidth, visHeight, theme);
                    break;
                case VisualizerMode.Stars:
                    DrawStarfieldVisualizer(visX, visY, visWidth, visHeight, theme);
                    break;
                case VisualizerMode.Matrix:
                    DrawMatrixVisualizer(visX, visY, visWidth, visHeight, theme);
                    break;
                case VisualizerMode.Equalizer:
                    DrawEqualizerVisualizer(visX, visY, visWidth, visHeight, theme);
                    break;
                case VisualizerMode.Spiral:
                    DrawSpiralVisualizer(visX, visY, visWidth, visHeight, theme);
                    break;
            }
        }

        static void DrawBarVisualizer(int x, int y, int width, int height, ThemeColors theme)
        {
            int barCount = Math.Min(width, 48);
            int barWidth = Math.Max(1, width / barCount);
            
            for (int i = 0; i < barCount; i++)
            {
                // Apply frequency response curve
                float freqResponse = i < barCount / 3 ? 1.3f : 
                                   i < barCount * 2 / 3 ? 1.1f : 0.9f;
                
                float intensity = spectrumData[i % spectrumData.Length] * freqResponse;
                
                // Add some dynamic movement (only when playing)
                float movement = isPaused ? 0f : (float)Math.Sin(animationFrame * 0.5f + i * 0.2f) * 0.1f;
                intensity = Math.Clamp(intensity + movement, 0, 1);
                
                int barHeight = (int)(intensity * height * 1.1f);
                
                for (int h = 0; h < barHeight; h++)
                {
                    int currentY = y + height - 1 - h;
                    if (currentY >= y && currentY < y + height)
                    {
                        char block;
                        if (h == barHeight - 1) 
                            block = '▀';
                        else if (h < barHeight * 0.7f)
                            block = '█';
                        else
                            block = '▓';
                        
                        ConsoleColor color = GetVisualizerColor((float)h / height, theme);
                        int barX = x + i * barWidth;
                        if (barX < x + width)
                        {
                            WriteToBuffer(barX, currentY, block.ToString(), color, theme.Background);
                            if (barWidth > 1 && barX + 1 < x + width)
                            {
                                WriteToBuffer(barX + 1, currentY, block.ToString(), color, theme.Background);
                            }
                        }
                    }
                }
                
                // Draw peak indicators (only when playing)
                if (!isPaused && barHeight > 0 && barHeight < height - 1)
                {
                    int peakY = y + height - 1 - barHeight;
                    WriteToBuffer(x + i * barWidth, peakY, "●", theme.Highlight, theme.Background);
                }
            }
        }

        static void DrawWaveVisualizer(int x, int y, int width, int height, ThemeColors theme)
        {
            int points = Math.Min(width * 2, audioData.Length);
            
            int[] wavePoints = new int[points];
            for (int i = 0; i < points; i++)
            {
                float pos = (float)i / points;
                float bassInfluence = audioData[(int)(pos * 0.2f * audioData.Length)] * 0.6f;
                float midInfluence = audioData[(int)(pos * 0.6f * audioData.Length)] * 0.8f;
                float trebleInfluence = audioData[(int)(pos * audioData.Length)] * 0.4f;
                
                float combined = (bassInfluence + midInfluence + trebleInfluence) / 3f;
                float movement = isPaused ? 0f : (float)Math.Sin(animationFrame * 0.8f + i * 0.02f) * 0.1f;
                float intensity = combined * (1f + movement);
                
                wavePoints[i] = y + height - 1 - (int)(intensity * height * 1.1f);
            }
            
            // Draw smoothed wave
            for (int i = 0; i < points - 2; i++)
            {
                int x1 = x + i / 2;
                int x2 = x + (i + 2) / 2;
                
                if (x1 >= x && x2 < x + width && x1 != x2)
                {
                    char waveChar = i % 4 == 0 ? '●' : '·';
                    DrawLine(x1, wavePoints[i], x2, wavePoints[i + 2], waveChar, theme.Visualizer1, theme);
                }
            }
            
            // Draw filled wave area (only when playing or during decay)
            if (!isPaused || pauseTimer > 0)
            {
                for (int i = 0; i < points - 1; i++)
                {
                    int currentX = x + i / 2;
                    if (currentX >= x && currentX < x + width)
                    {
                        int waveY = wavePoints[i];
                        int bottomY = y + height - 1;
                        
                        for (int fillY = waveY + 1; fillY <= bottomY; fillY++)
                        {
                            float fillRatio = (float)(fillY - waveY) / (bottomY - waveY);
                            char fillChar = fillRatio < 0.3f ? '░' : 
                                          fillRatio < 0.6f ? '▒' : '▓';
                            ConsoleColor fillColor = GetGradientColor(bottomY - fillY, bottomY - waveY, theme);
                            WriteToBuffer(currentX, fillY, fillChar.ToString(), fillColor, theme.Background);
                        }
                    }
                }
            }
        }

        static void DrawParticleVisualizer(int x, int y, int width, int height, ThemeColors theme)
        {
            foreach (var particle in particles)
            {
                int partX = (int)particle.X;
                int partY = (int)particle.Y;
                
                if (partX >= x && partX < x + width && partY >= y && partY < y + height)
                {
                    WriteToBuffer(partX, partY, particle.Character.ToString(), particle.Color, theme.Background);
                }
            }
        }

        static void DrawSpectrumVisualizer(int x, int y, int width, int height, ThemeColors theme)
        {
            int bandCount = Math.Min(width / 3, spectrumData.Length * 2);
            
            for (int i = 0; i < bandCount; i++)
            {
                float logPos = (float)Math.Log(i + 1) / (float)Math.Log(bandCount + 1);
                int dataIndex = (int)(logPos * spectrumData.Length);
                dataIndex = Math.Clamp(dataIndex, 0, spectrumData.Length - 1);
                
                float intensity = spectrumData[dataIndex];
                
                float response = dataIndex < spectrumData.Length / 4 ? 1.4f : 
                                dataIndex < spectrumData.Length / 2 ? 1.2f : 1.0f;
                
                intensity *= response;
                int barHeight = (int)(intensity * height * 1.2f);
                
                ConsoleColor color = GetVisualizerColor(intensity, theme);
                
                for (int h = 0; h < barHeight; h++)
                {
                    int currentY = y + height - 1 - h;
                    if (currentY >= y && currentY < y + height)
                    {
                        ConsoleColor bandColor = GetGradientColor(h, barHeight, theme);
                        int bandX = x + i * 3;
                        if (bandX < x + width - 2)
                        {
                            string block = GetSpectrumBlock(h, barHeight);
                            WriteToBuffer(bandX, currentY, block, bandColor, theme.Background);
                        }
                    }
                }
            }
        }

        static void DrawStarfieldVisualizer(int x, int y, int width, int height, ThemeColors theme)
        {
            foreach (var star in stars)
            {
                int starX = (int)star.X;
                int starY = (int)star.Y;
                
                if (starX >= x && starX < x + width && starY >= y && starY < y + height)
                {
                    char starChar = star.Brightness > 0.7f ? '★' : 
                                   star.Brightness > 0.4f ? '✦' : '•';
                    WriteToBuffer(starX, starY, starChar.ToString(), theme.Visualizer1, theme.Background);
                }
            }
        }

        static void DrawMatrixVisualizer(int x, int y, int width, int height, ThemeColors theme)
        {
            foreach (var mc in matrixChars)
            {
                int charX = (int)mc.X;
                int charY = (int)mc.Y;
                
                if (charX >= x && charX < x + width && charY >= y && charY < y + height)
                {
                    WriteToBuffer(charX, charY, mc.Character.ToString(), mc.Color, theme.Background);
                }
            }
        }

        static void DrawEqualizerVisualizer(int x, int y, int width, int height, ThemeColors theme)
        {
            int bandCount = Math.Min(width, 32);
            int bandWidth = Math.Max(1, width / bandCount);
            
            for (int i = 0; i < bandCount; i++)
            {
                float intensity = spectrumData[i % spectrumData.Length];
                int barHeight = (int)(intensity * height);
                ConsoleColor color = GetVisualizerColor(intensity, theme);
                
                for (int h = 0; h < barHeight; h++)
                {
                    int currentY = y + height - 1 - h;
                    int bandX = x + i * bandWidth;
                    if (bandX < x + width)
                    {
                        WriteToBuffer(bandX, currentY, "█", color, theme.Background);
                    }
                }
                
                if (i % 4 == 0 && barHeight > 2)
                {
                    string freqLabel = GetFrequencyLabel(i, bandCount);
                    WriteToBuffer(x + i * bandWidth, y + height, freqLabel, theme.Text, theme.Background);
                }
            }
        }

        static void DrawSpiralVisualizer(int x, int y, int width, int height, ThemeColors theme)
        {
            int centerX = x + width / 2;
            int centerY = y + height / 2;
            int maxRadius = Math.Min(width, height) / 2 - 2;
            
            for (int i = 0; i < spectrumData.Length; i++)
            {
                float intensity = spectrumData[i];
                float angle = (float)i / spectrumData.Length * MathF.PI * 2 + animationFrame * 0.1f;
                float radius = intensity * maxRadius;
                
                int pointX = centerX + (int)(Math.Cos(angle) * radius);
                int pointY = centerY + (int)(Math.Sin(angle) * radius);
                
                if (pointX >= x && pointX < x + width && pointY >= y && pointY < y + height)
                {
                    WriteToBuffer(pointX, pointY, "●", GetVisualizerColor(intensity, theme), theme.Background);
                }
            }
        }

        static string GetSpectrumBlock(int currentHeight, int totalHeight)
        {
            if (currentHeight == totalHeight - 1) return "▀";
            if (currentHeight > totalHeight * 0.8f) return "▓";
            if (currentHeight > totalHeight * 0.5f) return "▒";
            return "░";
        }

        static ConsoleColor GetGradientColor(int height, int maxHeight, ThemeColors theme)
        {
            float ratio = (float)height / maxHeight;
            if (ratio > 0.8f) return theme.Visualizer1;
            if (ratio > 0.5f) return theme.Visualizer2;
            return theme.Visualizer3;
        }

        static string GetFrequencyLabel(int band, int totalBands)
        {
            string[] labels = { "60", "250", "1K", "4K", "16K" };
            int index = (band * labels.Length) / totalBands;
            return index < labels.Length ? labels[index] : "";
        }

        static void DrawLine(int x1, int y1, int x2, int y2, char ch, ConsoleColor color, ThemeColors theme)
        {
            int dx = Math.Abs(x2 - x1);
            int dy = Math.Abs(y2 - y1);
            int sx = (x1 < x2) ? 1 : -1;
            int sy = (y1 < y2) ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if (x1 >= 0 && x1 < consoleWidth && y1 >= 0 && y1 < consoleHeight)
                {
                    WriteToBuffer(x1, y1, ch.ToString(), color, theme.Background);
                }

                if (x1 == x2 && y1 == y2) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x1 += sx; }
                if (e2 < dx) { err += dx; y1 += sy; }
            }
        }

        static char GetBlockChar(int currentHeight, int totalHeight)
        {
            if (currentHeight == totalHeight - 1) return '▀';
            if (currentHeight == 0) return '▄';
            return '█';
        }

        static ConsoleColor GetVisualizerColor(float intensity, ThemeColors theme)
        {
            if (intensity > 0.8f) return theme.Visualizer1;
            if (intensity > 0.5f) return theme.Visualizer2;
            return theme.Visualizer3;
        }

        static void DrawBorderToBuffer(int x, int y, int width, int height, ConsoleColor color, ThemeColors theme)
        {
            // Top border
            WriteToBuffer(x, y, "╔" + new string('═', width - 2) + "╗", color, theme.Background);
            
            // Side borders
            for (int i = 1; i < height - 1; i++)
            {
                WriteToBuffer(x, y + i, "║", color, theme.Background);
                WriteToBuffer(x + width - 1, y + i, "║", color, theme.Background);
            }
            
            // Bottom border
            WriteToBuffer(x, y + height - 1, "╚" + new string('═', width - 2) + "╝", color, theme.Background);
        }

        static void DrawHeaderToBuffer(int width, ThemeColors theme)
        {
            string[] logo = currentTheme switch
            {
                Theme.Lain => lainLogo,
                Theme.Cyberpunk => cyberpunkLogo,
                Theme.Matrix => matrixLogo,
                Theme.Retro => retroLogo,
                Theme.Neon => neonLogo,
                _ => lainLogo
            };

            // Draw logo centered
            int logoY = 2;
            for (int i = 0; i < logo.Length && logoY + i < 5; i++)
            {
                int x = width / 2 - logo[i].Length / 2;
                WriteToBuffer(x, logoY + i, logo[i], theme.Primary, theme.Background);
            }
        }

        static void DrawBoxToBuffer(int x, int y, int width, int height, string title, ConsoleColor borderColor, ThemeColors theme)
        {
            // Top border with title
            string topLeft = "╔";
            string topRight = "╗";
            string titleStr = $"═══ {title} ";
            int remainingWidth = width - titleStr.Length - 2;
            string topBorder = topLeft + titleStr + new string('═', remainingWidth) + topRight;
            WriteToBuffer(x, y, topBorder, borderColor, theme.Background);
            
            // Side borders
            for (int i = 1; i < height - 1; i++)
            {
                WriteToBuffer(x, y + i, "║", borderColor, theme.Background);
                WriteToBuffer(x + width - 1, y + i, "║", borderColor, theme.Background);
            }
            
            // Bottom border
            string bottomBorder = "╚" + new string('═', width - 2) + "╝";
            WriteToBuffer(x, y + height - 1, bottomBorder, borderColor, theme.Background);
        }

        static void DrawProgressBarToBuffer(int x, int y, int width, ThemeColors theme)
        {
            double progress = totalDuration > 0 ? Math.Clamp(currentPosition / totalDuration, 0, 1) : 0;
            int barWidth = width - 2;
            int filledWidth = (int)(barWidth * progress);
            
            // Draw bar background
            WriteToBuffer(x, y, "[", theme.ProgressBg, theme.Background);
            WriteToBuffer(x + 1, y, new string('─', barWidth), theme.ProgressBg, theme.Background);
            WriteToBuffer(x + barWidth + 1, y, "]", theme.ProgressBg, theme.Background);
            
            // Draw progress
            if (filledWidth > 0)
            {
                string progressBar = new string('█', filledWidth);
                WriteToBuffer(x + 1, y, progressBar, theme.Progress, theme.Background);
            }
            
            // Percentage
            string percentage = $" {progress * 100:0}%";
            WriteToBuffer(x + width + 2, y, percentage, theme.Text, theme.Background);
        }

        static void DrawVolumeBarToBuffer(int x, int y, int width, ThemeColors theme)
        {
            int barWidth = width - 2;
            int filledWidth = (int)(barWidth * (currentVolume / 100f));
            
            string volumeIcon = currentVolume == 0 ? "🔇" : 
                               currentVolume < 33 ? "🔈" :
                               currentVolume < 66 ? "🔉" : 
                               "🔊";
            
            // Draw bar
            WriteToBuffer(x, y, "[", theme.VolumeBg, theme.Background);
            WriteToBuffer(x + 1, y, new string('·', barWidth), theme.VolumeBg, theme.Background);
            WriteToBuffer(x + barWidth + 1, y, "]", theme.VolumeBg, theme.Background);
            
            // Draw volume level
            if (filledWidth > 0)
            {
                string volumeBar = new string('█', filledWidth);
                WriteToBuffer(x + 1, y, volumeBar, theme.Volume, theme.Background);
            }

            WriteToBuffer(x + width + 2, y, volumeIcon, theme.Volume, theme.Background);
        }

        static void DrawThemeSelectorToBuffer()
        {
            var theme = GetThemeColors(currentTheme);
            int width = consoleWidth;
            int height = consoleHeight;

            DrawBorderToBuffer(0, 0, width, height, theme.Border, theme);

            // Title
            string title = "═══ SELECT THEME ═══";
            WriteToBuffer(width / 2 - title.Length / 2, 2, title, theme.Primary, theme.Background);

            DrawBoxToBuffer(4, 4, width - 8, height - 8, "AVAILABLE THEMES", theme.Accent, theme);

            int startY = 6;
            string[] themeNames = { 
                "LAIN - Cyberpunk Anime Style", 
                "CYBERPUNK - Neon Futuristic",
                "MATRIX - Green Code Rain", 
                "SOLARIZED - Professional Dark",
                "DRACULA - Purple Elegance",
                "MONOKAI - Vibrant Contrast",
                "RETRO - 80s Computer Style",
                "NEON - Bright Neon Colors"
            };

            for (int i = 0; i < themeNames.Length; i++)
            {
                bool isSelected = i == selectedIndex;
                bool isCurrent = currentTheme == (Theme)i;

                ConsoleColor nameColor = isSelected ? theme.Highlight : 
                                       isCurrent ? theme.Success : theme.Text;

                string indicator = isSelected ? "▶ " : "  ";
                string status = isCurrent ? " [ACTIVE]" : "";

                WriteToBuffer(6, startY + i * 2, indicator + themeNames[i] + status, nameColor, theme.Background);
            }

            // Instructions
            string instructions = "ENTER: Apply Theme │ ESC: Back │ ↑/↓: Navigate";
            WriteToBuffer(width / 2 - instructions.Length / 2, height - 4, instructions, theme.Text, theme.Background);
        }

        static void DrawFileExplorerToBuffer()
        {
            var theme = GetThemeColors(currentTheme);
            int width = consoleWidth;
            int height = consoleHeight;

            DrawBorderToBuffer(0, 0, width, height, theme.Border, theme);

            string dirHeader = $"📁 {currentDirectory}";
            if (dirHeader.Length > width - 8)
                dirHeader = "📁 ..." + dirHeader.Substring(dirHeader.Length - (width - 12));
            WriteToBuffer(4, 1, dirHeader, theme.Primary, theme.Background);

            // Calculate pagination
            int startIndex = currentPage * itemsPerPage;
            int endIndex = Math.Min(startIndex + itemsPerPage, fileSystemEntries.Count);
            totalPages = (int)Math.Ceiling((double)fileSystemEntries.Count / itemsPerPage);

            WriteToBuffer(4, 2, $"Items: {fileSystemEntries.Count} │ Page {currentPage + 1}/{totalPages} │ Playlist: {playlist.Count} tracks", 
                         theme.Text, theme.Background);

            DrawBoxToBuffer(2, 3, width - 4, height - 10, "FILE SYSTEM", theme.Secondary, theme);

            int startY = 5;
            int listHeight = height - 14;

            for (int i = startIndex; i < endIndex; i++)
            {
                if (i - startIndex >= listHeight) break;
                
                var entry = fileSystemEntries[i];
                bool isSelected = i == selectedIndex;

                int currentY = startY + (i - startIndex);

                ConsoleColor color = isSelected ? theme.Highlight : 
                                   entry.IsDirectory ? theme.Primary : theme.Text;

                string indicator = isSelected ? "▶ " : "  ";
                string icon = entry.IsDirectory ? "📁" : "🎵";
                string name = entry.Name;
                if (name.Length > width - 15)
                    name = name.Substring(0, width - 18) + "...";

                WriteToBuffer(4, currentY, indicator + icon + " " + name, color, theme.Background);
            }

            // Page navigation
            if (totalPages > 1)
            {
                string pageInfo = $"[Page {currentPage + 1}/{totalPages}]";
                WriteToBuffer(width - pageInfo.Length - 2, height - 6, pageInfo, theme.Border, theme.Background);
            }

            // Controls
            DrawBoxToBuffer(2, height - 7, width - 4, 5, "CONTROLS", theme.Accent, theme);
            
            string controls = "ENTER: Open │ SPACE: Play │ A: Add Folder │ BACKSPACE: Back │ E: Player │ T: Themes │ Q: Quit";
            if (totalPages > 1)
            {
                controls = "ENTER: Open │ SPACE: Play │ A: Add │ BACK: Back │ E: Player │ PgUp/PgDn: Pages";
            }
            
            WriteToBuffer(4, height - 5, controls, theme.Text, theme.Background);
            
            // Status message
            if (!string.IsNullOrEmpty(statusMessage))
            {
                WriteToBuffer(4, height - 3, statusMessage, theme.Status, theme.Background);
            }
        }

        static void DrawAllTracksToBuffer()
        {
            var theme = GetThemeColors(currentTheme);
            int width = consoleWidth;
            int height = consoleHeight;

            DrawBorderToBuffer(0, 0, width, height, theme.Border, theme);

            WriteToBuffer(4, 1, $"🎵 PLAYLIST ({playlist.Count} TRACKS)", theme.Primary, theme.Background);

            DrawBoxToBuffer(2, 3, width - 4, height - 8, "ALL TRACKS", theme.Accent, theme);

            int startY = 5;
            int listHeight = height - 12;

            for (int i = 0; i < Math.Min(listHeight, playlist.Count); i++)
            {
                bool isSelected = i == selectedIndex;
                bool isPlaying = i == currentTrackIndex;

                int currentY = startY + i;

                ConsoleColor color = isSelected ? theme.Highlight : 
                                   isPlaying ? theme.Success : theme.Text;

                string indicator = isSelected ? "▶ " : "  ";
                string playing = isPlaying ? "♪ " : "  ";
                string name = Path.GetFileNameWithoutExtension(playlist[i]);
                if (name.Length > width - 20)
                    name = name.Substring(0, width - 23) + "...";

                WriteToBuffer(4, currentY, $"{indicator}{playing}{i + 1:00}. {name}", color, theme.Background);
            }

            // Controls
            DrawBoxToBuffer(2, height - 5, width - 4, 4, "CONTROLS", theme.Accent, theme);
            
            string controls = "ENTER: Play │ DELETE: Remove │ E: Player │ T: Themes │ Q: Quit";
            WriteToBuffer(4, height - 3, controls, theme.Text, theme.Background);
        }

        static void WriteToBuffer(int x, int y, string text, ConsoleColor foreground, ConsoleColor background)
        {
            if (y < 0 || y >= consoleHeight)
                return;

            for (int i = 0; i < text.Length; i++)
            {
                int posX = x + i;
                if (posX >= 0 && posX < consoleWidth)
                {
                    currentBuffer[y, posX] = text[i];
                    currentFgBuffer[y, posX] = foreground;
                    currentBgBuffer[y, posX] = background;
                }
            }
        }

        static string FormatTime(double seconds)
        {
            if (seconds <= 0 || double.IsNaN(seconds)) return "00:00";
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
        }

        static void HandleInput()
        {
            var key = Console.ReadKey(true).Key;

            if (showThemeSelector)
                HandleThemeSelectorInput(key);
            else if (showFileExplorer)
                HandleFileExplorerInput(key);
            else if (showAllTracks)
                HandleAllTracksInput(key);
            else
                HandlePlayerInput(key);
        }

        static void HandlePlayerInput(ConsoleKey key)
        {
            switch (key)
            {
                case ConsoleKey.Spacebar:
                    TogglePlayPause();
                    break;
                case ConsoleKey.RightArrow:
                    NextTrack();
                    break;
                case ConsoleKey.LeftArrow:
                    PreviousTrack();
                    break;
                case ConsoleKey.UpArrow:
                    AdjustVolume(5);
                    break;
                case ConsoleKey.DownArrow:
                    AdjustVolume(-5);
                    break;
                case ConsoleKey.Add:
                case ConsoleKey.OemPlus:
                    AdjustVisualizerIntensity(0.1f);
                    break;
                case ConsoleKey.Subtract:
                case ConsoleKey.OemMinus:
                    AdjustVisualizerIntensity(-0.1f);
                    break;
                case ConsoleKey.F:
                    ToggleShuffle();
                    break;
                case ConsoleKey.R:
                    repeatMode = !repeatMode;
                    needsRedraw = true;
                    ShowStatusMessage($"Repeat: {(repeatMode ? "ON" : "OFF")}");
                    break;
                case ConsoleKey.V:
                    showVisualizer = !showVisualizer;
                    needsRedraw = true;
                    ShowStatusMessage($"Visualizer: {(showVisualizer ? "ON" : "OFF")}");
                    break;
                case ConsoleKey.M:
                    CycleVisualizerMode();
                    break;
                case ConsoleKey.E:
                    ShowFileExplorer();
                    break;
                case ConsoleKey.A:
                    showAllTracks = true;
                    selectedIndex = currentTrackIndex;
                    needsRedraw = true;
                    break;
                case ConsoleKey.T:
                    showThemeSelector = true;
                    selectedIndex = (int)currentTheme;
                    needsRedraw = true;
                    break;
                case ConsoleKey.Q:
                    isRunning = false;
                    break;
            }
        }

        static void AdjustVisualizerIntensity(float delta)
        {
            visualizerIntensity = Math.Clamp(visualizerIntensity + delta, 0.5f, 2.0f);
            needsRedraw = true;
            ShowStatusMessage($"Visualizer Intensity: {visualizerIntensity:0.0}x");
        }

        static void CycleVisualizerMode()
        {
            int modeCount = Enum.GetValues(typeof(VisualizerMode)).Length;
            visualizerMode = (VisualizerMode)(((int)visualizerMode + 1) % modeCount);
            needsRedraw = true;
            ShowStatusMessage($"Visualizer Mode: {visualizerMode}");
        }

        static void HandleThemeSelectorInput(ConsoleKey key)
        {
            switch (key)
            {
                case ConsoleKey.UpArrow:
                    if (selectedIndex > 0)
                    {
                        selectedIndex--;
                        needsRedraw = true;
                    }
                    break;
                case ConsoleKey.DownArrow:
                    if (selectedIndex < availableThemes.Length - 1)
                    {
                        selectedIndex++;
                        needsRedraw = true;
                    }
                    break;
                case ConsoleKey.Enter:
                    currentTheme = availableThemes[selectedIndex];
                    showThemeSelector = false;
                    
                    // Force full redraw with new theme
                    var theme = GetThemeColors(currentTheme);
                    for (int y = 0; y < consoleHeight; y++)
                    {
                        for (int x = 0; x < consoleWidth; x++)
                        {
                            previousBuffer[y, x] = '\0';
                            previousFgBuffer[y, x] = ConsoleColor.Black;
                            previousBgBuffer[y, x] = ConsoleColor.Black;
                        }
                    }
                    
                    needsRedraw = true;
                    ShowStatusMessage($"Theme changed to: {currentTheme}");
                    break;
                case ConsoleKey.Escape:
                    showThemeSelector = false;
                    needsRedraw = true;
                    break;
            }
        }

        static void HandleFileExplorerInput(ConsoleKey key)
        {
            int startIndex = currentPage * itemsPerPage;
            int endIndex = Math.Min(startIndex + itemsPerPage, fileSystemEntries.Count);

            switch (key)
            {
                case ConsoleKey.UpArrow:
                    if (selectedIndex > startIndex)
                    {
                        selectedIndex--;
                        needsRedraw = true;
                    }
                    else if (currentPage > 0)
                    {
                        currentPage--;
                        selectedIndex = startIndex - 1;
                        needsRedraw = true;
                    }
                    break;
                case ConsoleKey.DownArrow:
                    if (selectedIndex < endIndex - 1)
                    {
                        selectedIndex++;
                        needsRedraw = true;
                    }
                    else if (currentPage < totalPages - 1)
                    {
                        currentPage++;
                        selectedIndex = startIndex + itemsPerPage;
                        needsRedraw = true;
                    }
                    break;
                case ConsoleKey.PageUp:
                    if (currentPage > 0)
                    {
                        currentPage--;
                        selectedIndex = Math.Min(selectedIndex, startIndex + itemsPerPage - 1);
                        needsRedraw = true;
                    }
                    break;
                case ConsoleKey.PageDown:
                    if (currentPage < totalPages - 1)
                    {
                        currentPage++;
                        selectedIndex = startIndex;
                        needsRedraw = true;
                    }
                    break;
                case ConsoleKey.Enter:
                    if (fileSystemEntries.Count > 0 && selectedIndex < fileSystemEntries.Count)
                    {
                        var entry = fileSystemEntries[selectedIndex];
                        if (entry.IsDirectory)
                        {
                            directoryHistory.Push(currentDirectory);
                            LoadDirectoryContents(entry.FullPath);
                            currentPage = 0;
                            selectedIndex = 0;
                            needsRedraw = true;
                        }
                        else
                        {
                            AddToPlaylist(entry.FullPath);
                            ShowStatusMessage($"Added: {Path.GetFileName(entry.FullPath)}");
                            needsRedraw = true;
                        }
                    }
                    break;
                case ConsoleKey.Spacebar:
                    if (fileSystemEntries.Count > 0 && selectedIndex < fileSystemEntries.Count && !fileSystemEntries[selectedIndex].IsDirectory)
                    {
                        var entry = fileSystemEntries[selectedIndex];
                        AddToPlaylist(entry.FullPath);
                        currentTrackIndex = playlist.Count - 1;
                        showFileExplorer = false;
                        PlayCurrentTrack();
                    }
                    break;
                case ConsoleKey.A:
                    if (fileSystemEntries.Count > 0 && selectedIndex < fileSystemEntries.Count && fileSystemEntries[selectedIndex].IsDirectory)
                    {
                        var entry = fileSystemEntries[selectedIndex];
                        AddFolderToPlaylist(entry.FullPath);
                        ShowStatusMessage($"Folder added: {entry.Name}");
                        needsRedraw = true;
                    }
                    break;
                case ConsoleKey.Backspace:
                    if (directoryHistory.Count > 0)
                    {
                        LoadDirectoryContents(directoryHistory.Pop());
                        currentPage = 0;
                        selectedIndex = 0;
                        needsRedraw = true;
                    }
                    break;
                case ConsoleKey.E:
                    showFileExplorer = false;
                    needsRedraw = true;
                    break;
                case ConsoleKey.T:
                    showThemeSelector = true;
                    selectedIndex = (int)currentTheme;
                    needsRedraw = true;
                    break;
                case ConsoleKey.Q:
                    isRunning = false;
                    break;
            }
        }

        static void HandleAllTracksInput(ConsoleKey key)
        {
            switch (key)
            {
                case ConsoleKey.UpArrow:
                    if (selectedIndex > 0)
                    {
                        selectedIndex--;
                        needsRedraw = true;
                    }
                    break;
                case ConsoleKey.DownArrow:
                    if (selectedIndex < playlist.Count - 1)
                    {
                        selectedIndex++;
                        needsRedraw = true;
                    }
                    break;
                case ConsoleKey.Enter:
                    if (playlist.Count > 0)
                    {
                        currentTrackIndex = selectedIndex;
                        showAllTracks = false;
                        PlayCurrentTrack();
                    }
                    break;
                case ConsoleKey.Delete:
                    if (playlist.Count > 0 && playlist.Count > selectedIndex)
                    {
                        string removedTrack = playlist[selectedIndex];
                        playlist.RemoveAt(selectedIndex);
                        if (currentTrackIndex >= playlist.Count)
                            currentTrackIndex = Math.Max(0, playlist.Count - 1);
                        if (selectedIndex >= playlist.Count)
                            selectedIndex = Math.Max(0, playlist.Count - 1);
                        ShowStatusMessage($"Removed: {Path.GetFileName(removedTrack)}");
                        needsRedraw = true;
                    }
                    break;
                case ConsoleKey.E:
                    showAllTracks = false;
                    needsRedraw = true;
                    break;
                case ConsoleKey.T:
                    showThemeSelector = true;
                    selectedIndex = (int)currentTheme;
                    needsRedraw = true;
                    break;
                case ConsoleKey.Q:
                    isRunning = false;
                    break;
            }
        }

        static void ShowStatusMessage(string message)
        {
            statusMessage = message;
            statusMessageTime = DateTime.Now;
            needsRedraw = true;
        }

        static void PlayCurrentTrack()
        {
            if (playlist.Count == 0) return;

            CleanUpCurrentProcess();

            string track = playlist[currentTrackIndex];
            currentFileName = Path.GetFileName(track);

            if (useMpv)
            {
                StartMpvProcess(track);
            }

            isPaused = false;
            visualizerPaused = false;
            pauseTimer = 0f;
            needsRedraw = true;
            ShowStatusMessage($"Now playing: {Path.GetFileNameWithoutExtension(track)}");
        }

        static void StartMpvProcess(string filePath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ipcSocketPath = $"mpv-{Guid.NewGuid()}";
            }
            else
            {
                ipcSocketPath = Path.Combine(Path.GetTempPath(), $"mpv-{Guid.NewGuid()}.socket");
            }

            string args = $"--no-video --input-ipc-server={ipcSocketPath} --volume={currentVolume} \"{filePath}\"";

            mpvProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "mpv",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            mpvProcess.Start();

            Thread.Sleep(2000);
            mpvIpcClient = new MpvIpcClient(ipcSocketPath);
            mpvIpcClient.Connect();
        }

        static void TogglePlayPause()
        {
            if (mpvIpcClient != null && mpvIpcClient.IsConnected)
            {
                mpvIpcClient.SendCommand(new { command = new object[] { "cycle", "pause" } });
                ShowStatusMessage(isPaused ? "Resumed" : "Paused");
            }
        }

        static void NextTrack()
        {
            if (playlist.Count == 0) return;

            if (shuffleMode && shuffleOrder.Count > 0)
            {
                currentShuffleIndex = (currentShuffleIndex + 1) % shuffleOrder.Count;
                currentTrackIndex = shuffleOrder[currentShuffleIndex];
            }
            else
            {
                currentTrackIndex = (currentTrackIndex + 1) % playlist.Count;
            }
            PlayCurrentTrack();
        }

        static void PreviousTrack()
        {
            if (playlist.Count == 0) return;

            if (shuffleMode && shuffleOrder.Count > 0)
            {
                currentShuffleIndex = (currentShuffleIndex - 1 + shuffleOrder.Count) % shuffleOrder.Count;
                currentTrackIndex = shuffleOrder[currentShuffleIndex];
            }
            else
            {
                currentTrackIndex = (currentTrackIndex - 1 + playlist.Count) % playlist.Count;
            }
            PlayCurrentTrack();
        }

        static void AdjustVolume(float delta)
        {
            float newVolume = Math.Clamp(currentVolume + delta, 0, 100);
            if (newVolume != currentVolume)
            {
                currentVolume = newVolume;
                volumeChanged = true;
                needsRedraw = true;
                ShowStatusMessage($"Volume: {currentVolume:0}%");
            }
        }

        static void ToggleShuffle()
        {
            shuffleMode = !shuffleMode;
            if (shuffleMode)
                GenerateShuffleOrder();
            needsRedraw = true;
            ShowStatusMessage($"Shuffle: {(shuffleMode ? "ON" : "OFF")}");
        }

        static void GenerateShuffleOrder()
        {
            shuffleOrder = Enumerable.Range(0, playlist.Count).OrderBy(x => Guid.NewGuid()).ToList();
            currentShuffleIndex = shuffleOrder.IndexOf(currentTrackIndex);
            if (currentShuffleIndex == -1) currentShuffleIndex = 0;
        }

        static void SetInitialDirectory()
        {
            currentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (!Directory.Exists(currentDirectory))
                currentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            LoadDirectoryContents(currentDirectory);
        }

        static void ShowFileExplorer()
        {
            showFileExplorer = true;
            showAllTracks = false;
            currentPage = 0;
            selectedIndex = 0;
            needsRedraw = true;
        }

        static void LoadDirectoryContents(string directory)
        {
            fileSystemEntries.Clear();
            currentDirectory = directory;

            // Parent directory
            if (Directory.GetParent(directory) != null)
            {
                fileSystemEntries.Add(new FileSystemEntry
                {
                    Name = "[..]",
                    FullPath = Directory.GetParent(directory)!.FullName,
                    IsDirectory = true,
                    IsParentDirectory = true
                });
            }

            // Directories
            try
            {
                var directories = Directory.GetDirectories(directory);
                foreach (var dir in directories)
                {
                    fileSystemEntries.Add(new FileSystemEntry
                    {
                        Name = $"[{Path.GetFileName(dir)}]",
                        FullPath = dir,
                        IsDirectory = true
                    });
                }

                // Files
                var files = Directory.GetFiles(directory);
                foreach (var file in files)
                {
                    if (supportedFormats.Contains(Path.GetExtension(file).ToLower()))
                    {
                        fileSystemEntries.Add(new FileSystemEntry
                        {
                            Name = Path.GetFileName(file),
                            FullPath = file,
                            IsDirectory = false
                        });
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                ShowStatusMessage("Access denied to this directory");
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Error loading directory: {ex.Message}");
            }
        }

        static void AddToPlaylist(string filePath)
        {
            if (!playlist.Contains(filePath))
            {
                playlist.Add(filePath);
            }
        }

        static void AddFolderToPlaylist(string folderPath)
        {
            try
            {
                int addedCount = 0;
                AddFilesFromFolderRecursive(folderPath, ref addedCount);
                ShowStatusMessage($"Added {addedCount} files from folder");
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Error adding folder: {ex.Message}");
            }
        }

        static void AddFilesFromFolderRecursive(string folderPath, ref int count)
        {
            try
            {
                foreach (var file in Directory.GetFiles(folderPath))
                {
                    if (supportedFormats.Contains(Path.GetExtension(file).ToLower()) && !playlist.Contains(file))
                    {
                        playlist.Add(file);
                        count++;
                    }
                }

                foreach (var dir in Directory.GetDirectories(folderPath))
                {
                    AddFilesFromFolderRecursive(dir, ref count);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we can't access
            }
        }

        static void LoadFilesFromArgs(string[] args)
        {
            foreach (var arg in args)
            {
                if (File.Exists(arg) && supportedFormats.Contains(Path.GetExtension(arg).ToLower()))
                {
                    AddToPlaylist(Path.GetFullPath(arg));
                }
                else if (Directory.Exists(arg))
                {
                    AddFolderToPlaylist(Path.GetFullPath(arg));
                }
            }
        }

        static bool IsMpvAvailable()
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "mpv",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                process?.WaitForExit(1000);
                return process?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        static void CleanUpCurrentProcess()
        {
            mpvIpcClient?.Dispose();
            mpvIpcClient = null;

            if (mpvProcess != null && !mpvProcess.HasExited)
            {
                try
                {
                    mpvProcess.Kill();
                    mpvProcess.WaitForExit(1000);
                }
                catch { }
            }
            mpvProcess = null;
        }

        static void CleanUp()
        {
            CleanUpCurrentProcess();
            Console.CursorVisible = true;
            Console.Clear();
            Console.ResetColor();
        }
    }
}
