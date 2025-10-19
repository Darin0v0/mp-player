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
        static Theme[] availableThemes = { Theme.Lain, Theme.Cyberpunk, Theme.Matrix, Theme.Solarized, Theme.Dracula, Theme.Monokai };

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

        // Low-level console buffer for flicker-free rendering
        private static CharInfo[] buffer;
        private static SmallRect rect;
        private static IntPtr consoleHandle;
        private static int consoleWidth, consoleHeight;

        // Theme definitions
        enum Theme
        {
            Lain,
            Cyberpunk,
            Matrix,
            Solarized,
            Dracula,
            Monokai
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
        }

        // Windows API structures for low-level console access
        [StructLayout(LayoutKind.Sequential)]
        public struct Coord
        {
            public short X;
            public short Y;
            public Coord(short X, short Y)
            {
                this.X = X;
                this.Y = Y;
            }
        };

        [StructLayout(LayoutKind.Explicit)]
        public struct CharUnion
        {
            [FieldOffset(0)] public char UnicodeChar;
            [FieldOffset(0)] public byte AsciiChar;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct CharInfo
        {
            [FieldOffset(0)] public CharUnion Char;
            [FieldOffset(2)] public short Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SmallRect
        {
            public short Left;
            public short Top;
            public short Right;
            public short Bottom;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteConsoleOutput(IntPtr hConsoleOutput, CharInfo[] lpBuffer, Coord dwBufferSize, Coord dwBufferCoord, ref SmallRect lpWriteRegion);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        const int STD_OUTPUT_HANDLE = -11;
        const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

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
                    Glitch = ConsoleColor.Red,
                    Warning = ConsoleColor.Yellow,
                    Success = ConsoleColor.Green
                },
                Theme.Cyberpunk => new ThemeColors
                {
                    Background = ConsoleColor.Black,
                    Text = ConsoleColor.White,
                    Primary = ConsoleColor.Blue,
                    Secondary = ConsoleColor.DarkBlue,
                    Accent = ConsoleColor.Magenta,
                    Border = ConsoleColor.DarkMagenta,
                    Highlight = ConsoleColor.Cyan,
                    Progress = ConsoleColor.Cyan,
                    ProgressBg = ConsoleColor.DarkGray,
                    Volume = ConsoleColor.Blue,
                    VolumeBg = ConsoleColor.DarkGray,
                    Status = ConsoleColor.Green,
                    Glitch = ConsoleColor.Yellow,
                    Warning = ConsoleColor.Yellow,
                    Success = ConsoleColor.Green
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
                    Success = ConsoleColor.White
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
                    Success = ConsoleColor.Green
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
                    Success = ConsoleColor.Green
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
                    Success = ConsoleColor.Green
                },
                _ => GetThemeColors(Theme.Lain)
            };
        }

        // Simple ASCII logos
        static readonly string[] lainLogo = {
            "  _                    _       ",
            " | |    __ _ _ __   __| | ___  ",
            " | |   / _` | '_ \\ / _` |/ _ \\ ",
            " | |__| (_| | | | | (_| | (_) |",
            " |_____\\__,_|_| |_|\\__,_|\\___/ "
        };

        static readonly string[] cyberpunkLogo = {
            "   ____      _                 _    ",
            "  / ___|   _| |__   ___ _ __  | | __",
            " | |  | | | | '_ \\ / _ \\ '__| | |/ /",
            " | |__| |_| | |_) |  __/ |    |   < ",
            "  \\____\\__, |_.__/ \\___|_|    |_|\\_\\",
            "       |___/                        "
        };

        static readonly string[] matrixLogo = {
            "  __  __       _   _             ",
            " |  \\/  | __ _| |_| |_ ___ _ __  ",
            " | |\\/| |/ _` | __| __/ _ \\ '__| ",
            " | |  | | (_| | |_| ||  __/ |    ",
            " |_|  |_|\\__,_|\\__|\\__\\___|_|    "
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
            // Initialize low-level console buffer
            InitializeConsoleBuffer();

            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
            Console.CursorVisible = false;
            Console.Title = "TERMINAL MUSIC PLAYER";
            Console.TreatControlCAsInput = true;

            try
            {
                useMpv = IsMpvAvailable();
                if (!useMpv)
                {
                    DrawErrorScreen();
                    return;
                }

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
                        InitializeConsoleBuffer();
                        
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

                    if (!string.IsNullOrEmpty(statusMessage) && (DateTime.Now - statusMessageTime).TotalSeconds > 3)
                    {
                        statusMessage = "";
                        needsRedraw = true;
                    }

                    if (volumeChanged && mpvIpcClient != null && mpvIpcClient.IsConnected)
                    {
                        ApplyVolumeToMpv();
                        volumeChanged = false;
                    }

                    if ((DateTime.Now - lastAnimationTime).TotalMilliseconds > 200)
                    {
                        animationFrame = (animationFrame + 1) % 4;
                        lastAnimationTime = DateTime.Now;
                        if (currentTheme == Theme.Lain || currentTheme == Theme.Matrix)
                            needsRedraw = true;
                    }

                    if (Console.KeyAvailable)
                    {
                        HandleInput();
                    }

                    Thread.Sleep(50);
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

        static void InitializeConsoleBuffer()
        {
            consoleWidth = Console.WindowWidth;
            consoleHeight = Console.WindowHeight;
            
            buffer = new CharInfo[consoleWidth * consoleHeight];
            rect = new SmallRect { Left = 0, Top = 0, Right = (short)consoleWidth, Bottom = (short)consoleHeight };
            consoleHandle = GetStdHandle(STD_OUTPUT_HANDLE);

            // Enable VT processing on Windows
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (GetConsoleMode(consoleHandle, out uint mode))
                {
                    SetConsoleMode(consoleHandle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
                }
            }
        }

        static void ClearBuffer()
        {
            var theme = GetThemeColors(currentTheme);
            short bgColor = (short)((int)theme.Background << 4);
            short fgColor = (short)(int)theme.Text;
            short attributes = (short)(fgColor | bgColor);

            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i].Attributes = attributes;
                buffer[i].Char.UnicodeChar = ' ';
            }
        }

        static void WriteToBuffer(int x, int y, string text, ConsoleColor foreground, ConsoleColor background)
        {
            if (x < 0 || x >= consoleWidth || y < 0 || y >= consoleHeight)
                return;

            short fgColor = (short)(int)foreground;
            short bgColor = (short)((int)background << 4);
            short attributes = (short)(fgColor | bgColor);

            int index = y * consoleWidth + x;
            for (int i = 0; i < text.Length && index + i < buffer.Length; i++)
            {
                if (x + i >= consoleWidth) break;
                
                buffer[index + i].Attributes = attributes;
                buffer[index + i].Char.UnicodeChar = text[i];
            }
        }

        static void RenderBuffer()
        {
            WriteConsoleOutput(consoleHandle, buffer,
                new Coord((short)consoleWidth, (short)consoleHeight),
                new Coord(0, 0),
                ref rect);
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

        static void DrawPlayer()
        {
            ClearBuffer();
            var theme = GetThemeColors(currentTheme);
            int width = consoleWidth;
            int height = consoleHeight;

            // Draw border
            DrawBorder(width, height, theme);

            // Draw header
            DrawHeader(width, theme);

            // Now Playing section
            DrawSection(2, 6, width - 4, 6, "NOW PLAYING", theme.Primary, theme);

            // Track name
            if (!string.IsNullOrEmpty(currentFileName))
            {
                string displayName = Path.GetFileNameWithoutExtension(currentFileName);
                int maxNameLength = width - 10;
                if (displayName.Length > maxNameLength)
                    displayName = displayName.Substring(0, maxNameLength - 3) + "...";
                
                WriteToBuffer(4, 8, "> " + displayName, theme.Text, theme.Background);
            }
            else
            {
                WriteToBuffer(4, 8, "> NO TRACK SELECTED", theme.Text, theme.Background);
            }

            // Status
            WriteToBuffer(4, 9, "Status: ", theme.Secondary, theme.Background);
            string statusText = isPaused ? "PAUSED" : "PLAYING";
            ConsoleColor statusColor = isPaused ? theme.Warning : theme.Success;
            WriteToBuffer(12, 9, statusText, statusColor, theme.Background);

            // Mode indicators
            string shuffleStatus = shuffleMode ? "[SHUFFLE ON] " : "[SHUFFLE OFF]";
            string repeatStatus = repeatMode ? "[REPEAT ON]" : "[REPEAT OFF]";
            WriteToBuffer(width - 25, 9, shuffleStatus, shuffleMode ? theme.Accent : theme.Border, theme.Background);
            WriteToBuffer(width - 12, 9, repeatStatus, repeatMode ? theme.Accent : theme.Border, theme.Background);

            // Progress section
            DrawSection(2, 12, width - 4, 4, "PROGRESS", theme.Secondary, theme);
            
            string timeDisplay = totalDuration > 0 ? 
                $"{FormatTime(currentPosition)} / {FormatTime(totalDuration)}" : 
                $"{FormatTime(currentPosition)} / --:--";
            WriteToBuffer(4, 14, timeDisplay, theme.Text, theme.Background);

            // Progress bar
            DrawProgressBar(4, 15, width - 8, theme);

            // Volume section
            DrawSection(2, 16, width - 4, 4, "VOLUME", theme.Accent, theme);
            
            WriteToBuffer(4, 18, $"Level: {currentVolume:0}%", theme.Text, theme.Background);
            DrawVolumeBar(4, 19, Math.Min(40, width - 12), theme);

            // Track info
            WriteToBuffer(4, 21, $"Track: {currentTrackIndex + 1} of {playlist.Count}", theme.Text, theme.Background);

            // Controls section
            DrawSection(2, 22, width - 4, height - 25, "CONTROLS", theme.Highlight, theme);

            // Control labels
            int controlsY = 24;
            string[][] controls = {
                new[] { "SPACE", "Play/Pause" },
                new[] { "RIGHT", "Next Track" },
                new[] { "LEFT", "Previous Track" },
                new[] { "UP/DOWN", "Volume" },
                new[] { "F", $"Shuffle ({(shuffleMode ? "ON" : "OFF")})" },
                new[] { "R", $"Repeat ({(repeatMode ? "ON" : "OFF")})" },
                new[] { "E", "File Explorer" },
                new[] { "A", "All Tracks" },
                new[] { "T", "Themes" },
                new[] { "Q", "Quit" }
            };

            for (int i = 0; i < controls.Length; i++)
            {
                if (controlsY + i >= height - 2) break;
                
                WriteToBuffer(4, controlsY + i, controls[i][0], theme.Highlight, theme.Background);
                WriteToBuffer(4 + controls[i][0].Length + 1, controlsY + i, $" - {controls[i][1]}", theme.Text, theme.Background);
            }

            // Footer
            string footerText = $"THEME: {currentTheme}";
            WriteToBuffer(width / 2 - footerText.Length / 2, height - 2, footerText, theme.Border, theme.Background);

            // Status message
            if (!string.IsNullOrEmpty(statusMessage))
            {
                WriteToBuffer(2, height - 4, statusMessage.PadRight(width - 4), theme.Status, theme.Background);
            }

            RenderBuffer();
        }

        static void DrawBorder(int width, int height, ThemeColors theme)
        {
            // Top border
            WriteToBuffer(0, 0, "+" + new string('-', width - 2) + "+", theme.Border, theme.Background);
            
            // Side borders
            for (int i = 1; i < height - 1; i++)
            {
                WriteToBuffer(0, i, "|", theme.Border, theme.Background);
                WriteToBuffer(width - 1, i, "|", theme.Border, theme.Background);
            }
            
            // Bottom border
            WriteToBuffer(0, height - 1, "+" + new string('-', width - 2) + "+", theme.Border, theme.Background);
        }

        static void DrawHeader(int width, ThemeColors theme)
        {
            string[] logo = currentTheme switch
            {
                Theme.Lain => lainLogo,
                Theme.Cyberpunk => cyberpunkLogo,
                Theme.Matrix => matrixLogo,
                _ => lainLogo
            };

            string title = currentTheme switch
            {
                Theme.Lain => "SERIAL EXPERIMENTS LAIN",
                Theme.Cyberpunk => "CYBERPUNK MODE",
                Theme.Matrix => "THE MATRIX",
                Theme.Solarized => "SOLARIZED",
                Theme.Dracula => "DRACULA",
                Theme.Monokai => "MONOKAI",
                _ => "TERMINAL PLAYER"
            };

            // Draw logo
            int logoY = 1;
            for (int i = 0; i < logo.Length && logoY + i < 4; i++)
            {
                int x = width / 2 - logo[i].Length / 2;
                WriteToBuffer(x, logoY + i, logo[i], theme.Primary, theme.Background);
            }

            // Draw title
            int titleX = width / 2 - title.Length / 2;
            WriteToBuffer(titleX, logo.Length + 1, title, theme.Primary, theme.Background);
        }

        static void DrawSection(int x, int y, int width, int height, string title, ConsoleColor borderColor, ThemeColors theme)
        {
            // Top border with title
            string topBorder = $"+-- {title} {new string('-', width - 6 - title.Length)}+";
            WriteToBuffer(x, y, topBorder, borderColor, theme.Background);
            
            // Side borders
            for (int i = 1; i < height - 1; i++)
            {
                WriteToBuffer(x, y + i, "|", borderColor, theme.Background);
                WriteToBuffer(x + width - 1, y + i, "|", borderColor, theme.Background);
            }
            
            // Bottom border
            string bottomBorder = "+" + new string('-', width - 2) + "+";
            WriteToBuffer(x, y + height - 1, bottomBorder, borderColor, theme.Background);
        }

        static void DrawProgressBar(int x, int y, int width, ThemeColors theme)
        {
            double progress = totalDuration > 0 ? Math.Clamp(currentPosition / totalDuration, 0, 1) : 0;
            int barWidth = width - 2;
            int filledWidth = (int)(barWidth * progress);
            
            // Draw bar background
            string bar = "[" + new string('-', barWidth) + "]";
            WriteToBuffer(x, y, bar, theme.ProgressBg, theme.Background);
            
            // Draw progress
            if (filledWidth > 0)
            {
                string progressBar = new string('#', filledWidth);
                WriteToBuffer(x + 1, y, progressBar, theme.Progress, theme.Background);
            }
            
            // Percentage
            string percentage = $"({progress * 100:0}%)";
            WriteToBuffer(x + width + 2, y, percentage, theme.Text, theme.Background);
        }

        static void DrawVolumeBar(int x, int y, int width, ThemeColors theme)
        {
            int barWidth = width - 2;
            int filledWidth = (int)(barWidth * (currentVolume / 100f));
            
            string volumeIcon = currentVolume == 0 ? "[MUTE]" : 
                               currentVolume < 33 ? "[LOW] " :
                               currentVolume < 66 ? "[MID] " : 
                               "[HIGH]";
            
            // Draw bar
            string bar = "[" + new string('.', barWidth) + "] " + volumeIcon;
            WriteToBuffer(x, y, bar, theme.VolumeBg, theme.Background);
            
            // Draw volume level
            if (filledWidth > 0)
            {
                string volumeBar = new string('|', filledWidth);
                WriteToBuffer(x + 1, y, volumeBar, theme.Volume, theme.Background);
            }
        }

        static void DrawThemeSelector()
        {
            ClearBuffer();
            var theme = GetThemeColors(currentTheme);
            int width = consoleWidth;
            int height = consoleHeight;

            DrawBorder(width, height, theme);

            // Title
            string title = "SELECT THEME";
            WriteToBuffer(width / 2 - title.Length / 2, 2, title, theme.Primary, theme.Background);

            DrawSection(4, 4, width - 8, height - 8, "AVAILABLE THEMES", theme.Accent, theme);

            int startY = 6;
            string[] themeNames = { 
                "LAIN - Cyberpunk Anime Style", 
                "CYBERPUNK - Neon Futuristic",
                "MATRIX - Green Code Rain", 
                "SOLARIZED - Professional Dark",
                "DRACULA - Purple Elegance",
                "MONOKAI - Vibrant Contrast"
            };

            for (int i = 0; i < themeNames.Length; i++)
            {
                bool isSelected = i == selectedIndex;
                bool isCurrent = currentTheme == (Theme)i;

                ConsoleColor nameColor = isSelected ? theme.Highlight : 
                                       isCurrent ? theme.Success : theme.Text;

                string indicator = isSelected ? "> " : "  ";
                string status = isCurrent ? " [ACTIVE]" : "";

                WriteToBuffer(6, startY + i * 2, indicator + themeNames[i] + status, nameColor, theme.Background);
            }

            // Instructions
            string instructions = "ENTER: Apply Theme | ESC: Back | UP/DOWN: Navigate";
            WriteToBuffer(width / 2 - instructions.Length / 2, height - 4, instructions, theme.Text, theme.Background);

            RenderBuffer();
        }

        static string FormatTime(double seconds)
        {
            if (seconds <= 0 || double.IsNaN(seconds)) return "00:00";
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
        }

        static void DrawFileExplorer()
        {
            ClearBuffer();
            var theme = GetThemeColors(currentTheme);
            int width = consoleWidth;
            int height = consoleHeight;

            DrawBorder(width, height, theme);

            WriteToBuffer(4, 1, $"FOLDER: {currentDirectory}", theme.Primary, theme.Background);

            // Calculate pagination
            int startIndex = currentPage * itemsPerPage;
            int endIndex = Math.Min(startIndex + itemsPerPage, fileSystemEntries.Count);
            totalPages = (int)Math.Ceiling((double)fileSystemEntries.Count / itemsPerPage);

            WriteToBuffer(4, 2, $"Items: {fileSystemEntries.Count} | Page {currentPage + 1}/{totalPages} | Playlist: {playlist.Count} tracks", 
                         theme.Text, theme.Background);

            DrawSection(2, 3, width - 4, height - 10, "FILE SYSTEM", theme.Secondary, theme);

            int startY = 5;
            int listHeight = height - 14;

            for (int i = startIndex; i < endIndex && i < startIndex + listHeight; i++)
            {
                var entry = fileSystemEntries[i];
                bool isSelected = i == selectedIndex;

                int currentY = startY + (i - startIndex);

                ConsoleColor color = isSelected ? theme.Highlight : 
                                   entry.IsDirectory ? theme.Primary : theme.Text;

                string indicator = isSelected ? "> " : "  ";
                string icon = entry.IsDirectory ? "[DIR] " : "[FILE]";
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
            DrawSection(2, height - 7, width - 4, 5, "CONTROLS", theme.Accent, theme);
            
            string controls = "ENTER: Open | SPACE: Play | A: Add Folder | BACKSPACE: Back | E: Player | T: Themes | Q: Quit";
            if (totalPages > 1)
            {
                controls += " | PGUP/PGDN: Navigate Pages";
            }
            
            WriteToBuffer(4, height - 5, controls, theme.Text, theme.Background);
            
            // Status message
            if (!string.IsNullOrEmpty(statusMessage))
            {
                WriteToBuffer(4, height - 3, statusMessage, theme.Status, theme.Background);
            }

            RenderBuffer();
        }

        static void DrawAllTracks()
        {
            ClearBuffer();
            var theme = GetThemeColors(currentTheme);
            int width = consoleWidth;
            int height = consoleHeight;

            DrawBorder(width, height, theme);

            WriteToBuffer(4, 1, $"PLAYLIST ({playlist.Count} TRACKS)", theme.Primary, theme.Background);

            DrawSection(2, 3, width - 4, height - 8, "ALL TRACKS", theme.Accent, theme);

            int startY = 5;
            int listHeight = height - 12;

            for (int i = 0; i < Math.Min(listHeight, playlist.Count); i++)
            {
                bool isSelected = i == selectedIndex;
                bool isPlaying = i == currentTrackIndex;

                int currentY = startY + i;

                ConsoleColor color = isSelected ? theme.Highlight : 
                                   isPlaying ? theme.Success : theme.Text;

                string indicator = isSelected ? "> " : "  ";
                string playing = isPlaying ? "> " : "  ";
                string name = Path.GetFileNameWithoutExtension(playlist[i]);
                if (name.Length > width - 20)
                    name = name.Substring(0, width - 23) + "...";

                WriteToBuffer(4, currentY, $"{indicator}{playing}{i + 1:00}. {name}", color, theme.Background);
            }

            // Controls
            DrawSection(2, height - 5, width - 4, 4, "CONTROLS", theme.Accent, theme);
            
            string controls = "ENTER: Play | DELETE: Remove | E: Player | T: Themes | Q: Quit";
            WriteToBuffer(4, height - 3, controls, theme.Text, theme.Background);

            RenderBuffer();
        }

        // Input handling and other methods remain the same as previous version
        // ... (including HandleInput, PlayCurrentTrack, TogglePlayPause, etc.)

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
                            ShowStatusMessage($"Added to playlist: {Path.GetFileName(entry.FullPath)}");
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
                        ShowStatusMessage($"Folder added to playlist: {entry.Name}");
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
                        ShowStatusMessage($"Removed from playlist: {Path.GetFileName(removedTrack)}");
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
                ShowStatusMessage(isPaused ? "Paused" : "Playing");
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
