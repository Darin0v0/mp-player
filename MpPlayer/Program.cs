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
        static int selectedIndex = 0;
        static bool needsRedraw = true;
        static string statusMessage = "";
        static DateTime statusMessageTime = DateTime.MinValue;

        // Theme system
        static Theme currentTheme = Theme.Lain;
        static Theme[] availableThemes = { Theme.Lain, Theme.SteinGateWhite, Theme.SteinGateBlack };

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

        // Buffer for reducing flickering
        static char[] screenBuffer;
        static ConsoleColor[] foregroundBuffer;
        static ConsoleColor[] backgroundBuffer;
        static int consoleWidth = 0;
        static int consoleHeight = 0;
        static bool bufferInitialized = false;

        // Theme definitions
        enum Theme
        {
            Lain,
            SteinGateWhite,
            SteinGateBlack
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
                    Highlight = ConsoleColor.DarkMagenta,
                    Progress = ConsoleColor.Magenta,
                    ProgressBg = ConsoleColor.DarkGray,
                    Volume = ConsoleColor.Cyan,
                    VolumeBg = ConsoleColor.DarkGray,
                    Status = ConsoleColor.Green,
                    Glitch = ConsoleColor.Red
                },
                Theme.SteinGateWhite => new ThemeColors
                {
                    Background = ConsoleColor.White,
                    Text = ConsoleColor.DarkBlue,
                    Primary = ConsoleColor.DarkCyan,
                    Secondary = ConsoleColor.DarkMagenta,
                    Accent = ConsoleColor.DarkRed,
                    Border = ConsoleColor.Gray,
                    Highlight = ConsoleColor.Blue,
                    Progress = ConsoleColor.DarkGreen,
                    ProgressBg = ConsoleColor.DarkGray,
                    Volume = ConsoleColor.DarkCyan,
                    VolumeBg = ConsoleColor.DarkGray,
                    Status = ConsoleColor.DarkGreen,
                    Glitch = ConsoleColor.Red
                },
                Theme.SteinGateBlack => new ThemeColors
                {
                    Background = ConsoleColor.Black,
                    Text = ConsoleColor.White,
                    Primary = ConsoleColor.Cyan,
                    Secondary = ConsoleColor.DarkCyan,
                    Accent = ConsoleColor.Magenta,
                    Border = ConsoleColor.DarkCyan,
                    Highlight = ConsoleColor.Blue,
                    Progress = ConsoleColor.Green,
                    ProgressBg = ConsoleColor.DarkGray,
                    Volume = ConsoleColor.Cyan,
                    VolumeBg = ConsoleColor.DarkGray,
                    Status = ConsoleColor.Green,
                    Glitch = ConsoleColor.Red
                },
                _ => GetThemeColors(Theme.Lain)
            };
        }

        // ASCII Art for Lain theme
        static readonly string[] lainLogo = {
            "▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓",
            "▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓",
            "▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓",
            "▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓",
            "▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓"
        };

        static readonly string[] glitchText = {
            "01001100 01000001 01001001 01001110",
            "CONNECTING TO THE WIRED...",
            "PROTOCOL: TCP/IP",
            "STATUS: ONLINE",
            "USER: LAIN_IWAKURA"
        };

        // IPC Client for MPV
        class MpvIpcClient : IDisposable
        {
            private string _socketPath;
            private Stream? _stream;
            private Thread? _readThread;
            private bool _isDisposed = false;

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
                        var pipeClient = new NamedPipeClientStream(".", _socketPath, PipeDirection.InOut);
                        pipeClient.Connect(3000);
                        _stream = pipeClient;
                    }
                    else
                    {
                        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
                        var endPoint = new UnixDomainSocketEndPoint(_socketPath);
                        socket.Connect(endPoint);
                        _stream = new NetworkStream(socket, true);
                    }

                    _readThread = new Thread(ReadMessages);
                    _readThread.IsBackground = true;
                    _readThread.Start();

                    // Enable property observation
                    SendCommand(new { command = new object[] { "observe_property", 1, "pause" } });
                    SendCommand(new { command = new object[] { "observe_property", 2, "time-pos" } });
                    SendCommand(new { command = new object[] { "observe_property", 3, "duration" } });
                    SendCommand(new { command = new object[] { "observe_property", 4, "volume" } });
                    
                    // Request initial volume
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
                        Debug.WriteLine($"Sent: {json}");
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
                    Debug.WriteLine($"Received: {message}");
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
                    // Handle get_property responses
                    else if (root.TryGetProperty("error", out JsonElement errorElement) && 
                             errorElement.ValueKind == JsonValueKind.String &&
                             errorElement.GetString() == "success")
                    {
                        if (root.TryGetProperty("data", out JsonElement dataElement) && 
                            dataElement.ValueKind == JsonValueKind.Number)
                        {
                            // This is likely a response to get_property volume
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
                _isDisposed = true;
                try
                {
                    _stream?.Close();
                    _stream?.Dispose();
                }
                catch { }
                
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try { File.Delete(_socketPath); } catch { }
                }
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
            Console.CursorVisible = false;
            Console.Title = "LAIN MUSIC PLAYER";
            Console.TreatControlCAsInput = true;

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

                while (isRunning)
                {
                    if (needsRedraw || Console.WindowWidth != consoleWidth || Console.WindowHeight != consoleHeight)
                    {
                        consoleWidth = Console.WindowWidth;
                        consoleHeight = Console.WindowHeight;
                        InitializeConsole();
                        
                        if (showThemeSelector)
                            DrawThemeSelector();
                        else if (showFileExplorer)
                            DrawFileExplorer();
                        else if (showAllTracks)
                            DrawAllTracks();
                        else
                            DrawPlayer();
                        
                        needsRedraw = false;
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

                    if (Console.KeyAvailable)
                    {
                        HandleInput();
                    }

                    Thread.Sleep(33); // ~30 FPS
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

        static void InitializeConsole()
        {
            Console.Clear();
            var theme = GetThemeColors(currentTheme);
            Console.BackgroundColor = theme.Background;
            Console.ForegroundColor = theme.Text;
            Console.Clear();
            
            // Initialize buffers
            int bufferSize = consoleWidth * consoleHeight;
            screenBuffer = new char[bufferSize];
            foregroundBuffer = new ConsoleColor[bufferSize];
            backgroundBuffer = new ConsoleColor[bufferSize];
            
            for (int i = 0; i < bufferSize; i++)
            {
                screenBuffer[i] = ' ';
                foregroundBuffer[i] = theme.Text;
                backgroundBuffer[i] = theme.Background;
            }
            bufferInitialized = true;
        }

        static void ApplyVolumeToMpv()
        {
            if (mpvIpcClient == null || !mpvIpcClient.IsConnected) return;

            try
            {
                mpvIpcClient.SendCommand(new { command = new object[] { "set_property", "volume", currentVolume } });
                Debug.WriteLine($"Volume set to: {currentVolume}");
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
            Console.WriteLine("\n\n    ⚠️  MPV NOT FOUND");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("\n    This player requires MPV to be installed.");
            Console.WriteLine("\n    Installation instructions:");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("    • Windows: winget install mpv");
            Console.WriteLine("    • macOS:   brew install mpv");
            Console.WriteLine("    • Linux:   sudo apt install mpv");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("\n    Press any key to exit...");
            Console.ReadKey();
        }

        static void DrawPlayer()
        {
            if (!bufferInitialized) return;
            
            var theme = GetThemeColors(currentTheme);
            int width = consoleWidth;
            int height = consoleHeight;

            Console.SetCursorPosition(0, 0);
            Console.BackgroundColor = theme.Background;
            Console.ForegroundColor = theme.Text;

            // Draw border with Lain-style glitch effect
            DrawLainBorder(width, height, theme);

            // Draw Lain-style header
            DrawLainHeader(width, theme);

            // Now Playing section
            DrawBox(2, 6, width - 4, 6, "PROTOCOL: AUDIO_STREAM", theme.Primary, theme);

            // Track name with glitch effect for Lain theme
            Console.SetCursorPosition(4, 8);
            Console.Write("🎵 ");
            if (!string.IsNullOrEmpty(currentFileName))
            {
                string displayName = Path.GetFileNameWithoutExtension(currentFileName);
                int maxNameLength = width - 10;
                if (displayName.Length > maxNameLength)
                    displayName = displayName.Substring(0, maxNameLength - 3) + "...";
                
                if (currentTheme == Theme.Lain && DateTime.Now.Second % 4 == 0)
                {
                    Console.ForegroundColor = theme.Glitch;
                    Console.Write(displayName);
                    Console.ForegroundColor = theme.Text;
                }
                else
                {
                    Console.Write(displayName);
                }
            }
            else
            {
                Console.Write("NO SIGNAL");
            }

            // Status with cyberpunk icons
            Console.SetCursorPosition(4, 9);
            Console.ForegroundColor = isPaused ? theme.Secondary : theme.Progress;
            string statusIcon = isPaused ? "⏸" : "▶";
            Console.Write($"{statusIcon} {(isPaused ? "STANDBY" : "TRANSMITTING")}");

            // Mode indicators
            Console.SetCursorPosition(width - 25, 9);
            Console.ForegroundColor = shuffleMode ? theme.Accent : theme.Border;
            Console.Write("🔀 SHUFFLE ");
            Console.ForegroundColor = repeatMode ? theme.Accent : theme.Border;
            Console.Write("🔁 REPEAT");

            // Progress section
            Console.SetCursorPosition(4, 11);
            Console.ForegroundColor = theme.Text;
            string timeDisplay = totalDuration > 0 ? 
                $"{FormatTime(currentPosition)} / {FormatTime(totalDuration)}" : 
                $"{FormatTime(currentPosition)} / --:--";
            Console.Write(timeDisplay);

            // Enhanced progress bar
            DrawEnhancedProgressBar(4, 12, width - 8, theme);

            // Volume section
            DrawBox(2, 14, width - 4, 5, "VOLUME CONTROL", theme.Secondary, theme);
            
            Console.SetCursorPosition(4, 16);
            Console.ForegroundColor = theme.Secondary;
            Console.Write("Level: ");
            Console.ForegroundColor = theme.Text;
            Console.Write($"{currentVolume:0}%");

            // Enhanced volume bar
            DrawEnhancedVolumeBar(4, 17, 30, theme);

            // Track info
            Console.SetCursorPosition(4, 19);
            Console.ForegroundColor = theme.Text;
            Console.Write($"TRACK {currentTrackIndex + 1:000} OF {playlist.Count:000}");

            // Controls section
            DrawBox(2, 20, width - 4, height - 23, "CONTROL PROTOCOL", theme.Accent, theme);

            // Cyberpunk-style controls layout
            int controlsStartY = 22;
            string[] leftControls = {
                " [SPACE]   PLAY/STANDBY",
                " [→]       NEXT_TRANSMISSION", 
                " [←]       PREV_TRANSMISSION",
                " [↑]       VOLUME_UP",
                " [↓]       VOLUME_DOWN"
            };

            string[] rightControls = {
                " [F]       SHUFFLE_PROTOCOL",
                " [R]       REPEAT_CYCLE",
                " [E]       FILE_SYSTEM",
                " [A]       ALL_TRANSMISSIONS",
                " [T]       THEME_SELECT",
                " [Q]       TERMINATE"
            };

            for (int i = 0; i < leftControls.Length; i++)
            {
                Console.SetCursorPosition(4, controlsStartY + i);
                Console.ForegroundColor = theme.Highlight;
                Console.Write(leftControls[i].Substring(0, 11));
                Console.ForegroundColor = theme.Text;
                Console.Write(leftControls[i].Substring(11));
            }

            for (int i = 0; i < rightControls.Length; i++)
            {
                Console.SetCursorPosition(width / 2, controlsStartY + i);
                Console.ForegroundColor = theme.Highlight;
                Console.Write(rightControls[i].Substring(0, 11));
                Console.ForegroundColor = theme.Text;
                Console.Write(rightControls[i].Substring(11));
            }

            // Footer with theme info and glitch text
            string themeName = currentTheme.ToString();
            Console.SetCursorPosition(width / 2 - 15, height - 2);
            Console.ForegroundColor = theme.Border;
            
            if (currentTheme == Theme.Lain && DateTime.Now.Second % 3 == 0)
            {
                Console.Write(glitchText[DateTime.Now.Second % glitchText.Length]);
            }
            else
            {
                Console.Write($"THEME: {themeName} | CONNECT THE WIRED");
            }

            // Status message
            if (!string.IsNullOrEmpty(statusMessage))
            {
                Console.SetCursorPosition(2, height - 4);
                Console.ForegroundColor = theme.Status;
                Console.Write(statusMessage.PadRight(width - 4));
            }
        }

        static void DrawLainBorder(int width, int height, ThemeColors theme)
        {
            // Top border with Lain-style glitch
            Console.SetCursorPosition(0, 0);
            Console.ForegroundColor = theme.Border;
            if (currentTheme == Theme.Lain && DateTime.Now.Millisecond < 100)
            {
                Console.ForegroundColor = theme.Glitch;
                Console.Write("╬" + new string('╬', width - 2) + "╬");
            }
            else
            {
                Console.Write("╔" + new string('═', width - 2) + "╗");
            }
            
            // Side borders
            for (int i = 1; i < height - 1; i++)
            {
                Console.SetCursorPosition(0, i);
                Console.Write("║");
                Console.SetCursorPosition(width - 1, i);
                Console.Write("║");
            }
            
            // Bottom border
            Console.SetCursorPosition(0, height - 1);
            if (currentTheme == Theme.Lain && DateTime.Now.Millisecond < 100)
            {
                Console.ForegroundColor = theme.Glitch;
                Console.Write("╬" + new string('╬', width - 2) + "╬");
            }
            else
            {
                Console.ForegroundColor = theme.Border;
                Console.Write("╚" + new string('═', width - 2) + "╝");
            }
        }

        static void DrawLainHeader(int width, ThemeColors theme)
        {
            // Lain-style logo or header
            Console.SetCursorPosition((width - lainLogo[0].Length) / 2, 1);
            Console.ForegroundColor = theme.Primary;
            
            for (int i = 0; i < Math.Min(3, lainLogo.Length); i++)
            {
                Console.SetCursorPosition((width - lainLogo[i].Length) / 2, 1 + i);
                if (currentTheme == Theme.Lain && DateTime.Now.Second % 5 == 0)
                {
                    Console.ForegroundColor = theme.Glitch;
                    Console.Write(lainLogo[i]);
                    Console.ForegroundColor = theme.Primary;
                }
                else
                {
                    Console.Write(lainLogo[i]);
                }
            }

            // Subtitle with cyberpunk style
            Console.SetCursorPosition((width - 40) / 2, 4);
            Console.ForegroundColor = theme.Secondary;
            string subtitle = "≫ SYSTEM: ONLINE ≪ PROTOCOL: MUSIC_PLAYER ≫ USER: LAIN";
            Console.Write(subtitle);
        }

        static void DrawBox(int x, int y, int width, int height, string title, ConsoleColor borderColor, ThemeColors theme)
        {
            Console.ForegroundColor = borderColor;
            
            // Top border with title
            string topBorder = "╔";
            if (!string.IsNullOrEmpty(title))
            {
                string titleText = $" {title} ";
                topBorder += titleText;
                topBorder += new string('═', width - 2 - titleText.Length);
            }
            else
            {
                topBorder += new string('═', width - 2);
            }
            topBorder += "╗";
            
            Console.SetCursorPosition(x, y);
            Console.Write(topBorder);
            
            // Side borders
            for (int i = 1; i < height - 1; i++)
            {
                Console.SetCursorPosition(x, y + i);
                Console.Write("║");
                Console.SetCursorPosition(x + width - 1, y + i);
                Console.Write("║");
            }
            
            // Bottom border
            Console.SetCursorPosition(x, y + height - 1);
            Console.Write("╚" + new string('═', width - 2) + "╝");
        }

        static void DrawEnhancedProgressBar(int x, int y, int width, ThemeColors theme)
        {
            double progress = totalDuration > 0 ? Math.Clamp(currentPosition / totalDuration, 0, 1) : 0;
            int barWidth = width - 2;
            int filledWidth = (int)(barWidth * progress);
            
            Console.SetCursorPosition(x, y);
            Console.ForegroundColor = theme.Border;
            Console.Write("[");
            
            Console.ForegroundColor = theme.Progress;
            for (int i = 0; i < filledWidth; i++)
            {
                // Add some glitch effect for Lain theme
                if (currentTheme == Theme.Lain && DateTime.Now.Millisecond < 50 && i % 3 == 0)
                {
                    Console.ForegroundColor = theme.Glitch;
                    Console.Write("█");
                    Console.ForegroundColor = theme.Progress;
                }
                else
                {
                    Console.Write("█");
                }
            }
            
            Console.ForegroundColor = theme.ProgressBg;
            Console.Write(new string('░', barWidth - filledWidth));
            
            Console.ForegroundColor = theme.Border;
            Console.Write("]");
            
            // Progress percentage
            string percentage = $"({progress * 100:0}%)";
            Console.SetCursorPosition(x + width + 2, y);
            Console.ForegroundColor = theme.Text;
            Console.Write(percentage);
        }

        static void DrawEnhancedVolumeBar(int x, int y, int width, ThemeColors theme)
        {
            int barWidth = width - 2;
            int filledWidth = (int)(barWidth * (currentVolume / 100f));
            
            string volumeIcon = currentVolume == 0 ? "🔇" : 
                               currentVolume < 33 ? "🔈" :
                               currentVolume < 66 ? "🔉" : 
                               "🔊";
            
            Console.SetCursorPosition(x, y);
            Console.ForegroundColor = theme.Border;
            Console.Write("[");
            
            Console.ForegroundColor = theme.Volume;
            Console.Write(new string('|', filledWidth));
            
            Console.ForegroundColor = theme.VolumeBg;
            Console.Write(new string('.', barWidth - filledWidth));
            
            Console.ForegroundColor = theme.Border;
            Console.Write($"] {volumeIcon}");
        }

        static void DrawThemeSelector()
        {
            var theme = GetThemeColors(currentTheme);
            int width = consoleWidth;
            int height = consoleHeight;

            Console.Clear();
            Console.BackgroundColor = theme.Background;
            Console.ForegroundColor = theme.Text;
            Console.Clear();

            DrawLainBorder(width, height, theme);

            // Title
            Console.SetCursorPosition((width - 20) / 2, 2);
            Console.ForegroundColor = theme.Primary;
            Console.Write("🎨 SELECT THEME");

            DrawBox(4, 4, width - 8, height - 8, "THEME PROTOCOL", theme.Accent, theme);

            int startY = 6;
            string[] themeNames = { "LAIN", "STEINGATE WHITE", "STEINGATE BLACK" };

            for (int i = 0; i < themeNames.Length; i++)
            {
                bool isSelected = i == selectedIndex;
                bool isCurrent = currentTheme == (Theme)i;

                Console.SetCursorPosition(6, startY + i * 2);

                if (isSelected)
                {
                    Console.BackgroundColor = theme.Highlight;
                    Console.ForegroundColor = theme.Background;
                    Console.Write(" ▶ ");
                }
                else
                {
                    Console.BackgroundColor = theme.Background;
                    Console.ForegroundColor = theme.Text;
                    Console.Write("   ");
                }

                Console.BackgroundColor = isSelected ? theme.Highlight : theme.Background;
                Console.ForegroundColor = isSelected ? theme.Background : theme.Text;

                string status = isCurrent ? " [ACTIVE]" : "";
                Console.Write($" {themeNames[i]}{status}");

                // Reset background
                Console.BackgroundColor = theme.Background;
            }

            // Instructions
            Console.SetCursorPosition(6, height - 4);
            Console.ForegroundColor = theme.Text;
            Console.Write("ENTER: APPLY THEME • ESC: BACK • ↑↓: NAVIGATE");
        }

        static string FormatTime(double seconds)
        {
            if (seconds <= 0 || double.IsNaN(seconds)) return "00:00";
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
        }

        static void DrawFileExplorer()
        {
            var theme = GetThemeColors(currentTheme);
            int width = consoleWidth;
            int height = consoleHeight;

            Console.Clear();
            Console.BackgroundColor = theme.Background;
            Console.ForegroundColor = theme.Text;
            Console.Clear();

            DrawLainBorder(width, height, theme);

            Console.SetCursorPosition(4, 1);
            Console.ForegroundColor = theme.Primary;
            Console.Write($"📁 {currentDirectory}");

            // Calculate pagination
            int startIndex = currentPage * itemsPerPage;
            int endIndex = Math.Min(startIndex + itemsPerPage, fileSystemEntries.Count);
            totalPages = (int)Math.Ceiling((double)fileSystemEntries.Count / itemsPerPage);

            Console.SetCursorPosition(4, 2);
            Console.ForegroundColor = theme.Text;
            Console.Write($"ITEMS: {fileSystemEntries.Count} | PAGE {currentPage + 1}/{totalPages} | PLAYLIST: {playlist.Count} TRACKS");

            DrawBox(2, 3, width - 4, height - 10, "FILE SYSTEM", theme.Secondary, theme);

            int startY = 5;
            int listHeight = height - 14;

            for (int i = startIndex; i < endIndex && i < startIndex + listHeight; i++)
            {
                var entry = fileSystemEntries[i];
                bool isSelected = i == selectedIndex;

                int currentY = startY + (i - startIndex);

                Console.SetCursorPosition(4, currentY);

                if (isSelected)
                {
                    Console.BackgroundColor = theme.Highlight;
                    Console.ForegroundColor = theme.Background;
                    Console.Write(" ▶ ");
                }
                else
                {
                    Console.BackgroundColor = theme.Background;
                    Console.ForegroundColor = theme.Text;
                    Console.Write("   ");
                }

                Console.BackgroundColor = isSelected ? theme.Highlight : theme.Background;
                Console.ForegroundColor = isSelected ? theme.Background : 
                                       entry.IsDirectory ? theme.Primary : theme.Text;

                string icon = entry.IsDirectory ? "📁" : "🎵";
                string name = entry.Name;
                if (name.Length > width - 15)
                    name = name.Substring(0, width - 18) + "...";

                Console.Write($" {icon} {name}");

                Console.BackgroundColor = theme.Background;
            }

            // Page navigation info
            if (totalPages > 1)
            {
                string pageInfo = $"[PAGE {currentPage + 1}/{totalPages}]";
                Console.SetCursorPosition(width - pageInfo.Length - 2, height - 6);
                Console.ForegroundColor = theme.Border;
                Console.Write(pageInfo);
            }

            // Footer with enhanced controls
            DrawBox(2, height - 7, width - 4, 5, "CONTROL PROTOCOL", theme.Accent, theme);
            
            string controls = "ENTER: OPEN • SPACE: PLAY • A: ADD FOLDER • BACKSPACE: BACK • E: PLAYER • T: THEMES • Q: TERMINATE";
            if (totalPages > 1)
            {
                controls += " • PGUP/PGDN: NAVIGATE PAGES";
            }
            
            Console.SetCursorPosition(4, height - 5);
            Console.ForegroundColor = theme.Text;
            Console.Write(controls);
            
            // Status message
            if (!string.IsNullOrEmpty(statusMessage))
            {
                Console.SetCursorPosition(4, height - 3);
                Console.ForegroundColor = theme.Status;
                Console.Write(statusMessage);
            }
        }

        static void DrawAllTracks()
        {
            var theme = GetThemeColors(currentTheme);
            int width = consoleWidth;
            int height = consoleHeight;

            Console.Clear();
            Console.BackgroundColor = theme.Background;
            Console.ForegroundColor = theme.Text;
            Console.Clear();

            DrawLainBorder(width, height, theme);

            Console.SetCursorPosition(4, 1);
            Console.ForegroundColor = theme.Primary;
            Console.Write($"🎵 TRANSMISSION QUEUE ({playlist.Count} TRACKS)");

            DrawBox(2, 3, width - 4, height - 8, "ALL TRANSMISSIONS", theme.Accent, theme);

            int startY = 5;
            int listHeight = height - 12;

            for (int i = 0; i < Math.Min(listHeight, playlist.Count); i++)
            {
                bool isSelected = i == selectedIndex;
                bool isPlaying = i == currentTrackIndex;

                int currentY = startY + i;

                Console.SetCursorPosition(4, currentY);

                if (isSelected)
                {
                    Console.BackgroundColor = theme.Highlight;
                    Console.ForegroundColor = theme.Background;
                    Console.Write(" ▶ ");
                }
                else
                {
                    Console.BackgroundColor = theme.Background;
                    Console.ForegroundColor = theme.Text;
                    Console.Write("   ");
                }

                Console.BackgroundColor = isSelected ? theme.Highlight : theme.Background;
                Console.ForegroundColor = isSelected ? theme.Background : 
                                       isPlaying ? theme.Progress : theme.Text;

                string playing = isPlaying ? "▶ " : "  ";
                string name = Path.GetFileNameWithoutExtension(playlist[i]);
                if (name.Length > width - 20)
                    name = name.Substring(0, width - 23) + "...";

                Console.Write($" {playing}{i + 1:00}. {name}");

                Console.BackgroundColor = theme.Background;
            }

            // Footer with enhanced controls
            DrawBox(2, height - 5, width - 4, 4, "CONTROL PROTOCOL", theme.Accent, theme);
            
            string controls = "ENTER: PLAY • DELETE: REMOVE • E: PLAYER • T: THEMES • Q: TERMINATE";
            Console.SetCursorPosition(4, height - 3);
            Console.ForegroundColor = theme.Text;
            Console.Write(controls);
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
                    AdjustVolume(10);
                    break;
                case ConsoleKey.DownArrow:
                    AdjustVolume(-10);
                    break;
                case ConsoleKey.F:
                    ToggleShuffle();
                    break;
                case ConsoleKey.R:
                    repeatMode = !repeatMode;
                    needsRedraw = true;
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
                    needsRedraw = true;
                    ShowStatusMessage($"PROTOCOL: THEME_CHANGED TO {currentTheme}");
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
                            ShowStatusMessage($"ADDED TO QUEUE: {Path.GetFileName(entry.FullPath)}");
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
                        ShowStatusMessage($"FOLDER ADDED TO QUEUE: {entry.Name}");
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
                        ShowStatusMessage($"REMOVED FROM QUEUE: {Path.GetFileName(removedTrack)}");
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

        // Player control methods
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
            ShowStatusMessage($"NOW PLAYING: {Path.GetFileNameWithoutExtension(track)}");
        }

        static void StartMpvProcess(string filePath)
        {
            // Create unique socket path
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ipcSocketPath = $"mpv-{Guid.NewGuid()}";
            }
            else
            {
                ipcSocketPath = Path.Combine(Path.GetTempPath(), $"mpv-{Guid.NewGuid()}.socket");
            }

            // Set volume in command line arguments
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

            // Wait for MPV to start and create socket
            Thread.Sleep(2000);
            mpvIpcClient = new MpvIpcClient(ipcSocketPath);
            mpvIpcClient.Connect();
        }

        static void TogglePlayPause()
        {
            if (mpvIpcClient != null && mpvIpcClient.IsConnected)
            {
                mpvIpcClient.SendCommand(new { command = new object[] { "cycle", "pause" } });
                ShowStatusMessage(isPaused ? "PROTOCOL: STANDBY" : "PROTOCOL: TRANSMITTING");
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
                ShowStatusMessage($"VOLUME: {currentVolume:0}%");
            }
        }

        static void ToggleShuffle()
        {
            shuffleMode = !shuffleMode;
            if (shuffleMode)
                GenerateShuffleOrder();
            needsRedraw = true;
            ShowStatusMessage($"SHUFFLE PROTOCOL: {(shuffleMode ? "ACTIVATED" : "DEACTIVATED")}");
        }

        static void GenerateShuffleOrder()
        {
            shuffleOrder = Enumerable.Range(0, playlist.Count).OrderBy(x => Guid.NewGuid()).ToList();
            currentShuffleIndex = shuffleOrder.IndexOf(currentTrackIndex);
            if (currentShuffleIndex == -1) currentShuffleIndex = 0;
        }

        // File system methods
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
                ShowStatusMessage("ACCESS DENIED TO THIS DIRECTORY");
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"ERROR LOADING DIRECTORY: {ex.Message}");
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
                ShowStatusMessage($"ADDED {addedCount} FILES FROM FOLDER");
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"ERROR ADDING FOLDER: {ex.Message}");
            }
        }

        static void AddFilesFromFolderRecursive(string folderPath, ref int count)
        {
            try
            {
                // Add files from current directory
                foreach (var file in Directory.GetFiles(folderPath))
                {
                    if (supportedFormats.Contains(Path.GetExtension(file).ToLower()) && !playlist.Contains(file))
                    {
                        playlist.Add(file);
                        count++;
                    }
                }

                // Recursively add files from subdirectories
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
