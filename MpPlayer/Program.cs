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
using System.Numerics;

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
        static bool showEqualizer = false;
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
        static float[] audioData = new float[64];
        static float[] spectrumData = new float[32];
        static Random random = new Random();
        static float visualizerIntensity = 1.0f;
        static VisualizerMode visualizerMode = VisualizerMode.Bars;
        static List<Particle> particles = new List<Particle>();
        static List<Star> stars = new List<Star>();

        // Screen buffer for flicker-free rendering - DOUBLE BUFFERING
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
            Matrix
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
                                            isPaused = dataElement.GetBoolean();
                                            needsRedraw = true;
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
                audioData[i] = (float)(random.NextDouble() * 0.5);
            }

            // Initialize stars for starfield visualizer
            for (int i = 0; i < 50; i++)
            {
                stars.Add(new Star
                {
                    X = (float)random.NextDouble() * consoleWidth,
                    Y = (float)random.NextDouble() * consoleHeight,
                    Speed = 0.1f + (float)random.NextDouble() * 0.5f,
                    Brightness = (float)random.NextDouble()
                });
            }
        }

        static void UpdateVisualizer()
        {
            if ((DateTime.Now - lastVisualizerUpdate).TotalMilliseconds < 50) return;
            
            lastVisualizerUpdate = DateTime.Now;

            // Generate random audio data (in real app, this would come from actual audio analysis)
            for (int i = 0; i < audioData.Length; i++)
            {
                // Simulate audio waves with some randomness
                float change = (float)(random.NextDouble() - 0.5) * 0.2f;
                audioData[i] = Math.Clamp(audioData[i] + change, 0, 1);
                
                // Add some periodic patterns
                float time = (float)DateTime.Now.TimeOfDay.TotalSeconds;
                audioData[i] += (float)(Math.Sin(time * 2 + i * 0.5) * 0.1);
                audioData[i] = Math.Clamp(audioData[i], 0, 1);
            }

            // Update spectrum data (simulated FFT)
            for (int i = 0; i < spectrumData.Length; i++)
            {
                spectrumData[i] = audioData[i * 2] * visualizerIntensity;
            }

            // Update particles
            UpdateParticles();

            // Update stars for starfield
            if (visualizerMode == VisualizerMode.Stars)
            {
                UpdateStars();
            }
        }

        static void UpdateParticles()
        {
            // Add new particles based on audio intensity
            if (particles.Count < 100 && random.NextDouble() > 0.7)
            {
                float intensity = audioData[random.Next(audioData.Length)];
                if (intensity > 0.3)
                {
                    particles.Add(new Particle
                    {
                        X = consoleWidth / 2,
                        Y = consoleHeight - 5,
                        VelocityX = (float)(random.NextDouble() - 0.5) * 2f,
                        VelocityY = -(float)(random.NextDouble() * 2 + 1),
                        Life = random.Next(20, 60),
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
                particle.VelocityY += 0.1f; // gravity
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
            for (int i = 0; i < stars.Count; i++)
            {
                var star = stars[i];
                star.Y += star.Speed;
                if (star.Y >= consoleHeight)
                {
                    star.Y = 0;
                    star.X = (float)random.NextDouble() * consoleWidth;
                }
                stars[i] = star;
            }
        }

        static char GetParticleChar()
        {
            char[] chars = { '•', '·', '°', '∗', '⋅', '∘', '∙' };
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

            // Animation updates - reduced frequency
            if ((DateTime.Now - lastAnimationTime).TotalMilliseconds > 500)
            {
                animationFrame = (animationFrame + 1) % 4;
                lastAnimationTime = DateTime.Now;
                if (currentTheme == Theme.Lain || currentTheme == Theme.Matrix)
                    needsRedraw = true;
            }
        }

        static void Render()
        {
            if (!needsRedraw) return;

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

            // Visualizer section
            if (showVisualizer)
            {
                DrawVisualizerToBuffer(2, 6, width - 4, 8, theme);
            }

            // Now Playing section
            int nowPlayingY = showVisualizer ? 15 : 6;
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

            // Mode indicators
            string shuffleIcon = shuffleMode ? "🔀" : "▶";
            string repeatIcon = repeatMode ? "🔁" : "▶";
            WriteToBuffer(width - 24, nowPlayingY + 3, shuffleIcon + " SHUFFLE", shuffleMode ? theme.Accent : theme.Border, theme.Background);
            WriteToBuffer(width - 12, nowPlayingY + 3, repeatIcon + " REPEAT", repeatMode ? theme.Accent : theme.Border, theme.Background);

            // Progress section
            int progressY = nowPlayingY + 6;
            DrawBoxToBuffer(2, progressY, width - 4, 4, "PROGRESS", theme.Secondary, theme);
            
            string timeDisplay = totalDuration > 0 ? 
                $"{FormatTime(currentPosition)} / {FormatTime(totalDuration)}" : 
                $"{FormatTime(currentPosition)} / --:--";
            WriteToBuffer(4, progressY + 2, timeDisplay, theme.Text, theme.Background);

            // Progress bar
            DrawProgressBarToBuffer(4, progressY + 3, width - 8, theme);

            // Volume section
            int volumeY = progressY + 6;
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
            string footerText = $"│ THEME: {currentTheme} │ VISUALIZER: {visualizerMode} │ FPS: {currentFps:0.0} │";
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
            DrawBoxToBuffer(x, y, width, height, $"VISUALIZER - {visualizerMode}", theme.Visualizer1, theme);

            int visX = x + 2;
            int visY = y + 2;
            int visWidth = width - 4;
            int visHeight = height - 4;

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
            }
        }

        static void DrawBarVisualizer(int x, int y, int width, int height, ThemeColors theme)
        {
            int barCount = Math.Min(width, audioData.Length);
            int barWidth = Math.Max(1, width / barCount);
            
            for (int i = 0; i < barCount; i++)
            {
                float intensity = audioData[i] * visualizerIntensity;
                int barHeight = (int)(intensity * height);
                
                for (int h = 0; h < barHeight; h++)
                {
                    int currentY = y + height - 1 - h;
                    if (currentY >= y && currentY < y + height)
                    {
                        char block = GetBlockChar(h, barHeight);
                        ConsoleColor color = GetVisualizerColor(intensity, theme);
                        WriteToBuffer(x + i * barWidth, currentY, block.ToString(), color, theme.Background);
                    }
                }
            }
        }

        static void DrawWaveVisualizer(int x, int y, int width, int height, ThemeColors theme)
        {
            int points = Math.Min(width, audioData.Length);
            int[] wavePoints = new int[points];
            
            for (int i = 0; i < points; i++)
            {
                wavePoints[i] = y + height - 1 - (int)(audioData[i] * height);
            }
            
            for (int i = 0; i < points - 1; i++)
            {
                DrawLine(x + i, wavePoints[i], x + i + 1, wavePoints[i + 1], '●', theme.Visualizer1, theme);
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
            int bandCount = Math.Min(width / 2, spectrumData.Length);
            
            for (int i = 0; i < bandCount; i++)
            {
                float intensity = spectrumData[i];
                int barHeight = (int)(intensity * height);
                ConsoleColor color = GetVisualizerColor(intensity, theme);
                
                for (int h = 0; h < barHeight; h++)
                {
                    int currentY = y + height - 1 - h;
                    WriteToBuffer(x + i * 2, currentY, "█", color, theme.Background);
                    WriteToBuffer(x + i * 2 + 1, currentY, "█", color, theme.Background);
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
            // Simple matrix-like rain effect
            for (int i = 0; i < width; i += 2)
            {
                if (random.NextDouble() > 0.7)
                {
                    int length = random.Next(3, 8);
                    int startY = y + random.Next(height);
                    
                    for (int j = 0; j < length && startY + j < y + height; j++)
                    {
                        char symbol = j == 0 ? '█' : 
                                     j == 1 ? '▓' : 
                                     j == 2 ? '▒' : '░';
                        ConsoleColor color = j == 0 ? theme.Visualizer1 :
                                           j == 1 ? theme.Visualizer2 : theme.Visualizer3;
                        
                        WriteToBuffer(x + i, startY + j, symbol.ToString(), color, theme.Background);
                    }
                }
            }
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
