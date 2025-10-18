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
        static int selectedIndex = 0;
        static bool needsRedraw = true;
        static string previousScreen = "";

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

        // Progress tracking
        static string ipcSocketPath = "";
        static double currentPosition = 0;
        static double totalDuration = 0;
        static bool useMpv = false;

        // Buffer for reducing flickering
        static StringBuilder screenBuffer = new StringBuilder();
        static int consoleWidth = 0;
        static int consoleHeight = 0;

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
            Console.Title = "Terminal Music Player";
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
                        
                        if (showFileExplorer)
                            DrawFileExplorer();
                        else if (showAllTracks)
                            DrawAllTracks();
                        else
                            DrawPlayer();
                        needsRedraw = false;
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
            PrepareScreenBuffer();
            int width = consoleWidth;
            int height = consoleHeight;

            // Draw border
            DrawBorder(width, height);

            // Header with decorative elements
            AddString((width - 27) / 2, 1, "🎵 TERMINAL MUSIC PLAYER 🎵", ConsoleColor.Cyan);
            
            // Subtitle
            AddString((width - 17) / 2, 2, "♪ ♫ ♬ ♪ ♫ ♬ ♪ ♫ ♬", ConsoleColor.DarkCyan);

            // Now Playing section with box
            DrawBox(2, 4, width - 4, 8, "NOW PLAYING", ConsoleColor.Yellow);
            
            AddString(4, 6, "🎵 ", ConsoleColor.White);
            if (!string.IsNullOrEmpty(currentFileName))
            {
                string displayName = Path.GetFileNameWithoutExtension(currentFileName);
                int maxNameLength = width - 10;
                if (displayName.Length > maxNameLength)
                    displayName = displayName.Substring(0, maxNameLength - 3) + "...";
                AddString(7, 6, displayName, ConsoleColor.White);
            }
            else
            {
                AddString(7, 6, "No track selected", ConsoleColor.Gray);
            }

            // Status with icons
            AddString(4, 7, isPaused ? "⏸" : "▶", isPaused ? ConsoleColor.Yellow : ConsoleColor.Green);
            AddString(6, 7, isPaused ? "PAUSED" : "PLAYING", isPaused ? ConsoleColor.Yellow : ConsoleColor.Green);

            // Mode indicators
            AddString(width - 25, 7, "🔀", shuffleMode ? ConsoleColor.Green : ConsoleColor.DarkGray);
            AddString(width - 23, 7, "SHUFFLE", shuffleMode ? ConsoleColor.Green : ConsoleColor.DarkGray);
            AddString(width - 14, 7, "🔁", repeatMode ? ConsoleColor.Green : ConsoleColor.DarkGray);
            AddString(width - 12, 7, "REPEAT", repeatMode ? ConsoleColor.Green : ConsoleColor.DarkGray);

            // Progress section
            AddString(4, 9, FormatTime(currentPosition), ConsoleColor.Gray);
            AddString(4 + FormatTime(currentPosition).Length + 3, 9, "/", ConsoleColor.DarkGray);
            
            string totalTime = totalDuration > 0 ? FormatTime(totalDuration) : "--:--";
            AddString(4 + FormatTime(currentPosition).Length + 5, 9, totalTime, ConsoleColor.Gray);

            // Enhanced progress bar
            DrawEnhancedProgressBar(4, 10, width - 8);

            // Volume section with box
            DrawBox(2, 12, width - 4, 6, "VOLUME", ConsoleColor.Cyan);
            
            AddString(4, 14, "Level: ", ConsoleColor.Cyan);
            AddString(11, 14, $"{currentVolume:0}%", ConsoleColor.White);

            // Enhanced volume bar
            DrawEnhancedVolumeBar(4, 15, 30);

            // Track info
            AddString(4, 17, $"Track {currentTrackIndex + 1} of {playlist.Count}", ConsoleColor.Gray);

            // Controls section with box
            DrawBox(2, 19, width - 4, height - 22, "CONTROLS", ConsoleColor.DarkCyan);

            // Two-column controls layout
            int controlsStartY = 21;
            string[] leftControls = {
                " [SPACE]   Play/Pause",
                " [→]       Next Track", 
                " [←]       Previous Track",
                " [↑]       Volume +10%",
                " [↓]       Volume -10%"
            };

            string[] rightControls = {
                " [F]       Shuffle",
                " [R]       Repeat",
                " [E]       File Explorer",
                " [A]       All Tracks",
                " [Q]       Quit"
            };

            for (int i = 0; i < leftControls.Length; i++)
            {
                AddString(4, controlsStartY + i, leftControls[i].Substring(0, 11), ConsoleColor.White);
                AddString(15, controlsStartY + i, leftControls[i].Substring(11), ConsoleColor.Gray);
            }

            for (int i = 0; i < rightControls.Length; i++)
            {
                AddString(width / 2, controlsStartY + i, rightControls[i].Substring(0, 11), ConsoleColor.White);
                AddString(width / 2 + 11, controlsStartY + i, rightControls[i].Substring(11), ConsoleColor.Gray);
            }

            // Footer
            AddString(width / 2 - 10, height - 2, "Made with ♥ in C#", ConsoleColor.DarkGray);

            RenderScreenBuffer();
        }

        static void PrepareScreenBuffer()
        {
            screenBuffer.Clear();
            // Fill buffer with spaces
            for (int y = 0; y < consoleHeight; y++)
            {
                for (int x = 0; x < consoleWidth; x++)
                {
                    screenBuffer.Append(' ');
                }
                if (y < consoleHeight - 1)
                    screenBuffer.Append('\n');
            }
        }

        static void AddString(int x, int y, string text, ConsoleColor color = ConsoleColor.White)
        {
            if (y < 0 || y >= consoleHeight || x >= consoleWidth) return;
            
            int position = y * (consoleWidth + 1) + x;
            int length = Math.Min(text.Length, consoleWidth - x);
            
            if (position + length <= screenBuffer.Length && position >= 0)
            {
                screenBuffer.Remove(position, length);
                screenBuffer.Insert(position, text.Substring(0, length));
            }
        }

        static void RenderScreenBuffer()
        {
            Console.SetCursorPosition(0, 0);
            Console.Write(screenBuffer.ToString());
        }

        static void DrawBorder(int width, int height)
        {
            // Top border
            AddString(0, 0, "╔" + new string('═', width - 2) + "╗", ConsoleColor.DarkCyan);
            
            // Side borders
            for (int i = 1; i < height - 1; i++)
            {
                AddString(0, i, "║", ConsoleColor.DarkCyan);
                AddString(width - 1, i, "║", ConsoleColor.DarkCyan);
            }
            
            // Bottom border
            AddString(0, height - 1, "╚" + new string('═', width - 2) + "╝", ConsoleColor.DarkCyan);
        }

        static void DrawBox(int x, int y, int width, int height, string title, ConsoleColor color)
        {
            // Top border with title
            string topBorder = "╔";
            if (!string.IsNullOrEmpty(title))
            {
                string titleText = $" {title} ";
                topBorder += titleText + new string('═', width - 2 - titleText.Length);
            }
            else
            {
                topBorder += new string('═', width - 2);
            }
            topBorder += "╗";
            AddString(x, y, topBorder, color);
            
            // Side borders
            for (int i = 1; i < height - 1; i++)
            {
                AddString(x, y + i, "║", color);
                AddString(x + width - 1, y + i, "║", color);
            }
            
            // Bottom border
            AddString(x, y + height - 1, "╚" + new string('═', width - 2) + "╝", color);
        }

        static void DrawEnhancedProgressBar(int x, int y, int width)
        {
            double progress = totalDuration > 0 ? Math.Clamp(currentPosition / totalDuration, 0, 1) : 0;
            int barWidth = width - 2;
            int filledWidth = (int)(barWidth * progress);
            
            string progressBar = new string('█', filledWidth) + new string('░', barWidth - filledWidth);
            
            AddString(x, y, "[", ConsoleColor.DarkGray);
            AddString(x + 1, y, progressBar.Substring(0, filledWidth), ConsoleColor.Green);
            AddString(x + 1 + filledWidth, y, progressBar.Substring(filledWidth), ConsoleColor.DarkGray);
            AddString(x + 1 + barWidth, y, "]", ConsoleColor.DarkGray);
            
            // Progress percentage
            string percentage = $"({progress * 100:0}%)";
            AddString(x + width + 2, y, percentage, ConsoleColor.Gray);
        }

        static void DrawEnhancedVolumeBar(int x, int y, int width)
        {
            int barWidth = width - 2;
            int filledWidth = (int)(barWidth * (currentVolume / 100f));
            
            string volumeBar = new string('|', filledWidth) + new string('·', barWidth - filledWidth);
            
            string volumeIcon = currentVolume == 0 ? "🔇" : 
                               currentVolume < 33 ? "🔈" :
                               currentVolume < 66 ? "🔉" : 
                               "🔊";
            
            AddString(x, y, "[", ConsoleColor.DarkGray);
            AddString(x + 1, y, volumeBar.Substring(0, filledWidth), ConsoleColor.Cyan);
            AddString(x + 1 + filledWidth, y, volumeBar.Substring(filledWidth), ConsoleColor.DarkGray);
            AddString(x + 1 + barWidth, y, $"] {volumeIcon}", ConsoleColor.DarkGray);
        }

        static string FormatTime(double seconds)
        {
            if (seconds <= 0 || double.IsNaN(seconds)) return "00:00";
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
        }

        static void DrawFileExplorer()
        {
            PrepareScreenBuffer();
            int width = consoleWidth;
            int height = consoleHeight;

            DrawBorder(width, height);

            AddString(4, 1, $"📁 {currentDirectory}", ConsoleColor.Cyan);

            AddString(4, 2, $"Items: {fileSystemEntries.Count} | Playlist: {playlist.Count} tracks", ConsoleColor.Gray);

            DrawBox(2, 3, width - 4, height - 8, "FILE EXPLORER", ConsoleColor.Cyan);

            int startY = 5;
            int listHeight = height - 12;

            for (int i = 0; i < Math.Min(listHeight, fileSystemEntries.Count); i++)
            {
                var entry = fileSystemEntries[i];
                bool isSelected = i == selectedIndex;

                int currentY = startY + i;
                string prefix = isSelected ? " ▶ " : "   ";
                ConsoleColor prefixColor = isSelected ? ConsoleColor.White : ConsoleColor.Black;
                ConsoleColor bgColor = isSelected ? ConsoleColor.DarkBlue : ConsoleColor.Black;
                ConsoleColor textColor = entry.IsDirectory ? ConsoleColor.Cyan : ConsoleColor.White;

                AddString(4, currentY, prefix, prefixColor, bgColor);
                
                string icon = entry.IsDirectory ? "📁" : "🎵";
                string name = entry.Name;
                if (name.Length > width - 15)
                    name = name.Substring(0, width - 18) + "...";

                AddString(7, currentY, $"{icon} {name}", textColor, bgColor);
            }

            // Scroll indicator
            if (fileSystemEntries.Count > listHeight)
            {
                AddString(width - 4, height - 6, "↕", ConsoleColor.DarkGray);
            }

            // Footer with enhanced controls
            DrawBox(2, height - 5, width - 4, 4, "CONTROLS", ConsoleColor.DarkYellow);
            
            string controls = "Enter: Open/Add  •  Space: Play  •  Backspace: Back  •  E: Player  •  Q: Quit";
            AddString(4, height - 3, controls, ConsoleColor.White);
            
            RenderScreenBuffer();
        }

        static void AddString(int x, int y, string text, ConsoleColor foreground, ConsoleColor background)
        {
            // For simplicity in this implementation, we'll just use foreground color
            // In a more advanced implementation, you could handle background colors too
            AddString(x, y, text, foreground);
        }

        static void DrawAllTracks()
        {
            PrepareScreenBuffer();
            int width = consoleWidth;
            int height = consoleHeight;

            DrawBorder(width, height);

            AddString(4, 1, $"🎵 PLAYLIST ({playlist.Count} tracks)", ConsoleColor.Cyan);

            DrawBox(2, 3, width - 4, height - 8, "ALL TRACKS", ConsoleColor.Magenta);

            int startY = 5;
            int listHeight = height - 12;

            for (int i = 0; i < Math.Min(listHeight, playlist.Count); i++)
            {
                bool isSelected = i == selectedIndex;
                bool isPlaying = i == currentTrackIndex;

                int currentY = startY + i;
                string prefix = isSelected ? " ▶ " : "   ";
                ConsoleColor prefixColor = isSelected ? ConsoleColor.White : ConsoleColor.Black;
                ConsoleColor bgColor = isSelected ? ConsoleColor.DarkBlue : ConsoleColor.Black;
                ConsoleColor textColor = isPlaying ? ConsoleColor.Yellow : ConsoleColor.White;

                AddString(4, currentY, prefix, prefixColor, bgColor);
                
                string playing = isPlaying ? "▶ " : "  ";
                string name = Path.GetFileNameWithoutExtension(playlist[i]);
                if (name.Length > width - 20)
                    name = name.Substring(0, width - 23) + "...";

                AddString(7, currentY, $"{playing}{i + 1:00}. {name}", textColor, bgColor);
            }

            // Scroll indicator
            if (playlist.Count > listHeight)
            {
                AddString(width - 4, height - 6, "↕", ConsoleColor.DarkGray);
            }

            // Footer with enhanced controls
            DrawBox(2, height - 5, width - 4, 4, "CONTROLS", ConsoleColor.DarkYellow);
            
            string controls = "Enter: Play  •  Delete: Remove  •  E: Player  •  Q: Quit";
            AddString(4, height - 3, controls, ConsoleColor.White);
            
            RenderScreenBuffer();
        }

        static void HandleInput()
        {
            var key = Console.ReadKey(true).Key;

            if (showFileExplorer)
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
                case ConsoleKey.Q:
                    isRunning = false;
                    break;
            }
        }

        static void HandleFileExplorerInput(ConsoleKey key)
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
                    if (selectedIndex < fileSystemEntries.Count - 1)
                    {
                        selectedIndex++;
                        needsRedraw = true;
                    }
                    break;
                case ConsoleKey.Enter:
                    if (fileSystemEntries.Count > 0)
                    {
                        var entry = fileSystemEntries[selectedIndex];
                        if (entry.IsDirectory)
                        {
                            directoryHistory.Push(currentDirectory);
                            LoadDirectoryContents(entry.FullPath);
                            selectedIndex = 0;
                            needsRedraw = true;
                        }
                        else
                        {
                            AddToPlaylist(entry.FullPath);
                            needsRedraw = true;
                        }
                    }
                    break;
                case ConsoleKey.Spacebar:
                    if (fileSystemEntries.Count > 0 && !fileSystemEntries[selectedIndex].IsDirectory)
                    {
                        var entry = fileSystemEntries[selectedIndex];
                        AddToPlaylist(entry.FullPath);
                        currentTrackIndex = playlist.Count - 1;
                        showFileExplorer = false;
                        PlayCurrentTrack();
                    }
                    break;
                case ConsoleKey.Backspace:
                    if (directoryHistory.Count > 0)
                    {
                        LoadDirectoryContents(directoryHistory.Pop());
                        selectedIndex = 0;
                        needsRedraw = true;
                    }
                    break;
                case ConsoleKey.E:
                    showFileExplorer = false;
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
                        playlist.RemoveAt(selectedIndex);
                        if (currentTrackIndex >= playlist.Count)
                            currentTrackIndex = Math.Max(0, playlist.Count - 1);
                        if (selectedIndex >= playlist.Count)
                            selectedIndex = Math.Max(0, playlist.Count - 1);
                        needsRedraw = true;
                    }
                    break;
                case ConsoleKey.E:
                    showAllTracks = false;
                    needsRedraw = true;
                    break;
                case ConsoleKey.Q:
                    isRunning = false;
                    break;
            }
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
            }
        }

        static void ToggleShuffle()
        {
            shuffleMode = !shuffleMode;
            if (shuffleMode)
                GenerateShuffleOrder();
            needsRedraw = true;
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
                foreach (var dir in Directory.GetDirectories(directory))
                {
                    fileSystemEntries.Add(new FileSystemEntry
                    {
                        Name = $"[{Path.GetFileName(dir)}]",
                        FullPath = dir,
                        IsDirectory = true
                    });
                }

                // Files
                foreach (var file in Directory.GetFiles(directory))
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
                // Ignore directories we can't access
            }
        }

        static void AddToPlaylist(string filePath)
        {
            if (!playlist.Contains(filePath))
            {
                playlist.Add(filePath);
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