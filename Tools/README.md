# yt-dlp

The bundled `yt-dlp-plugins/videodownloader/yt_dlp_plugins/extractor/threads.py`
adds public Threads post extraction through yt-dlp's
[plugin mechanism](https://github.com/yt-dlp/yt-dlp#plugins).
It resolves share links using the canonical post URL and matches the post code
in public link-preview JSON, never falling back to unrelated recommendations.
For mixed-media posts it selects the first video. DASH renditions supply actual
resolution and audio tracks; dimensionless progressive URLs remain unknown-quality.
This source code is copied to build/publish output and loaded by yt-dlp.exe,
so end users do not need a separate Python installation. Core updates preserve
the plugin; changes to the Threads page format may require an application update.
Only public content is handled; account cookies are not read.

Plugin tests (developer Python environment with the `yt-dlp` package):
`python -m unittest discover -s Tests/ThreadsExtractor -v`.

On window startup, the application checks the local executable with `--version`
and queries the official GitHub latest stable release. It downloads only when
missing, invalid or older; an equal/newer local version is retained.

Runtime location: `Tools/yt-dlp.exe` under `AppContext.BaseDirectory` (for example,
`bin/Debug/net10.0/Tools/yt-dlp.exe`). The directory must be writable to install or
update. A source-tree binary is optional and ignored by Git. If supplied, the
project copies it to build/publish output as an initial offline seed.

Updates fetch the executable and SHA2-256SUMS from the same pinned official
release, verify SHA256, and check the downloaded executable's version before an
atomic replacement. The prior executable is retained as `yt-dlp.exe.bak`.
Interrupted/failed updates keep a previously verified working local version;
without one, downloads remain disabled. Closing the window cancels the check.
The status badge shows progress, readiness or fallback; hover for error details.

Tests: `dotnet run --project Tests/UpdaterSmoke -- Tools/yt-dlp.exe`
(or pass another real yt-dlp executable). These use deterministic HTTP fixtures
and real version probes to test install, no-op, fallback, validation and backup.

Video downloads support MP4 and MKV with source-derived resolution choices.
Only resolutions exposed by non-DRM source formats are listed, including 4K/8K
when available. Portrait videos use the shorter dimension. The selected format
IDs enforce that resolution; separate audio and video streams are merged.
Containers are remuxed without upscaling or video transcoding.

Audio-only downloads support MP3 and WAV. MP3 offers best variable quality and
96/128/192/256/320 kbps encoding. WAV offers 16-bit or 24-bit PCM at the source
sample rate, plus 16-bit/44.1 kHz and 24-bit/48 kHz presets. Increasing output
bit depth, sample rate or bitrate cannot improve the source. Conversion uses
an intermediate PCM WAV so matching codecs cannot bypass quality settings.

FFmpeg/ffprobe are checked at startup. A working existing installation is reused;
otherwise the GyanD/codexffmpeg essentials ZIP is downloaded and checked against
the GitHub release asset SHA256. Only the two executables and accompanying
LICENSE/README are extracted into a staging directory, tested, then installed at
`%LOCALAPPDATA%/VideoDownloader/Tools/ffmpeg`. No system PATH modification is made.
The Gyan build is linked by https://ffmpeg.org/download.html. This currently
installs missing/broken tools; it does not automatically upgrade working FFmpeg.

Each download uses its own staging directory, cleaned on completion/failure or
cancellation when filesystem permissions allow. Completed files are moved to the
chosen folder, with numeric suffixes for duplicates. Settings and the last 100
history records are stored under `%LOCALAPPDATA%/VideoDownloader`.

Some websites require additional runtime dependencies or authentication.
The application displays yt-dlp's error output when extraction/download fails.
Upstream dependency details: https://github.com/yt-dlp/yt-dlp#dependencies

Validation: built with zero warnings/errors; a local HTTP fixture was used with
the real executable to check metadata extraction, MP4/MKV output, MP3 bitrate, WAV PCM bit depth/sample rate and audio-only streams, source resolution choices,
progress events, invalid URL rejection and cancellation. This does not verify
live social-platform availability or native window interactions.
