# 🎵 Terminal Music & Video Player

A powerful terminal-based audio and video player built with C# and .NET 8.0. Supports multiple audio and video formats with an intuitive interface and advanced file management features.

## ✨ Features

- **Multi-format Support**: MP3, WAV, OGG, FLAC, MP4, AVI, MKV, MOV
- **Video Playback**: Full video support using ffplay
- **Interactive File Search**: Search for media files across your system
- **Directory Browser**: Browse and load files from directories
- **Playlist Support**: Load files from .m3u or .txt playlists
- **Advanced Controls**: Play/pause, next/previous, volume control, restart
- **Progress Visualization**: Visual progress bar with time display
- **Volume Control**: Real-time volume adjustment with +/- keys
- **Error Handling**: Robust error handling with graceful fallbacks
- **Cross-platform**: Works on Linux, Windows, and macOS

## 🚀 Installation

### Prerequisites

1. **Install .NET 8.0 SDK**:
   ```bash
   # Fedora/RHEL
   sudo dnf install dotnet-sdk-8.0
   
   # Ubuntu/Debian
   sudo apt install dotnet-sdk-8.0
   
   # macOS
   brew install dotnet
   ```

2. **Install FFmpeg** (required for audio/video processing):
   ```bash
   # Fedora/RHEL
   sudo dnf install ffmpeg
   
   # Ubuntu/Debian
   sudo apt install ffmpeg
   
   # macOS
   brew install ffmpeg
   ```

3. **Audio System** (Linux):
   ```bash
   # ALSA audio system (usually pre-installed)
   # For better volume control, ensure aplay is available
   which aplay
   
   # If not available, install ALSA utilities
   sudo dnf install alsa-utils  # Fedora/RHEL
   sudo apt install alsa-utils  # Ubuntu/Debian
   ```

### Build and Run

1. **Clone or download the project**
2. **Navigate to the project directory**:
   ```bash
   cd MpPlayer
   ```

3. **Build the project**:
   ```bash
   dotnet build
   ```

4. **Run the player**:
   ```bash
   # Interactive mode (recommended)
   dotnet run
   
   # Direct file playback
   dotnet run "path/to/audio.mp3"
   
   # Play multiple files
   dotnet run "song1.mp3" "song2.mp3" "video.mp4"
   
   # Play all files in a directory
   dotnet run "/path/to/music/folder"
   ```

## 🎮 Controls

### Main Menu
- **1** - Search for media files
- **2** - Browse directories
- **3** - Load from playlist
- **4** - Exit

### Search Mode
- **↑↓** - Navigate through results
- **Enter** - Select file
- **A** - Add all files to playlist
- **Esc** - Back to main menu

### Playback Controls
| Key | Action |
|-----|--------|
| **Space** | Play/Pause |
| **N** | Next Track |
| **P** | Previous Track |
| **S** | Stop |
| **R** | Restart Current Track |
| **+** | Increase Volume |
| **-** | Decrease Volume |
| **M** | Return to Main Menu |
| **Q** | Quit |

## 📁 Supported Formats

### Audio Formats
- MP3 (.mp3)
- WAV (.wav)
- OGG (.ogg)
- FLAC (.flac)

### Video Formats
- MP4 (.mp4)
- AVI (.avi)
- MKV (.mkv)
- MOV (.mov)

## 🔍 File Search Features

The player includes a powerful file search system that can:

- **Search by filename**: Find files containing specific text
- **Search by extension**: Find all files of a specific type
- **System-wide search**: Searches in home directory, /home, and /media
- **Interactive selection**: Navigate through results and select files
- **Batch add**: Add all search results to playlist at once

### Search Examples
```
# Search for all MP3 files
mp3

# Search for specific artist
metallica

# Search for specific album
dark side

# Search for video files
mp4
```

## 📋 Playlist Support

The player can load playlists from:
- **M3U files** (.m3u, .m3u8)
- **Text files** (.txt) with one file path per line
- **Comments** starting with # are ignored

### Example Playlist Format
```
# My Music Playlist
/home/user/Music/song1.mp3
/home/user/Music/song2.mp3
/home/user/Videos/movie.mp4
```

## 🔧 Technical Details

- **Framework**: .NET 8.0
- **Audio/Video Processing**: FFmpeg/FFplay
- **UI**: Console-based with Unicode symbols
- **Memory Management**: Automatic cleanup of processes
- **Cross-platform**: No Windows-specific dependencies

## 🐛 Troubleshooting

### Common Issues

1. **"ffmpeg is not installed"**
   - Install FFmpeg using your package manager
   - Ensure it's available in your system PATH

2. **"No supported files found"**
   - Check file extensions are supported
   - Verify file paths are correct
   - Ensure files are not corrupted

3. **Audio/Video not playing**
   - Check if FFmpeg can process the file: `ffmpeg -i file.mp3`
   - Verify file permissions
   - Try with a different file format

4. **Search not finding files**
   - Check file permissions in search directories
   - Try searching with different terms
   - Some directories may be excluded for security reasons

5. **High CPU usage**
   - This is normal for video files
   - Audio-only files use minimal resources

### Performance Tips

- For large directories, consider using specific file extensions
- Video files use more resources than audio files
- The player automatically skips corrupted files
- Search results are limited to 100 files for performance

## 🎯 Examples

```bash
# Start interactive mode
dotnet run

# Play your music collection
dotnet run ~/Music

# Play specific files
dotnet run "~/Downloads/song.mp3" "~/Videos/movie.mp4"

# Play from multiple sources
dotnet run "~/Music/Rock" "~/Music/Jazz" "~/Videos"

# Quick test with system sounds (if available)
dotnet run /usr/share/sounds
```

## 🆕 New Features in This Version

- **Interactive Menu System**: Easy navigation and file selection
- **System-wide File Search**: Find media files across your computer
- **Smooth Volume Control**: Real-time volume adjustment with ffmpeg + aplay pipeline
- **Improved Stop/Start**: Better process management
- **Playlist Support**: Load from M3U and text files
- **Enhanced UI**: Better progress display and controls
- **Fallback Audio System**: Automatic fallback to ffplay if aplay fails
- **Cross-platform Audio**: Works on Linux with ALSA audio system

## 🤝 Contributing

Feel free to submit issues and enhancement requests!

## 📄 License

This project is open source and available under the MIT License. 