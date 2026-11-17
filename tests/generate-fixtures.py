"""Generate small, real codec fixtures; no user media is read or modified."""
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FFMPEG = next((ROOT / 'vendor').glob('ffmpeg-*/bin/ffmpeg.exe'))
FFPROBE = FFMPEG.with_name('ffprobe.exe')
DEST = ROOT / 'tests' / 'media'
DEST.mkdir(exist_ok=True)

cases = [
    ('h264-8bit.mp4', 'libx264', 'yuv420p', []),
    ('h264-10bit.mp4', 'libx264', 'yuv420p10le', []),
    ('h265-10bit.mp4', 'libx265', 'yuv420p10le', ['-x265-params', 'log-level=error:pools=2']),
    ('h265-422-10bit.mp4', 'libx265', 'yuv422p10le', ['-x265-params', 'log-level=error:pools=2']),
    ('prores-422-10bit.mov', 'prores_ks', 'yuv422p10le', ['-profile:v', '3']),
    ('prores-4444.mov', 'prores_ks', 'yuv444p10le', ['-profile:v', '4']),
    ('prueba ñ — vídeo 10 bits.mp4', 'libx265', 'yuv420p10le', ['-x265-params', 'log-level=error:pools=2']),
    ('hdr10.mp4', 'libx265', 'yuv420p10le', ['-x265-params', 'log-level=error:pools=2', '-color_primaries', 'bt2020', '-color_trc', 'smpte2084', '-colorspace', 'bt2020nc']),
]
results = []
for name, codec, pixel_format, options in cases:
    path = DEST / name
    command = [str(FFMPEG), '-y', '-hide_banner', '-loglevel', 'error', '-f', 'lavfi', '-i', 'testsrc2=size=640x360:rate=24:duration=2', '-f', 'lavfi', '-i', 'sine=frequency=440:sample_rate=48000:duration=2', '-c:v', codec, '-pix_fmt', pixel_format, '-threads', '2', *options, '-c:a', 'aac', '-b:a', '96k', '-shortest', str(path)]
    subprocess.run(command, check=True)
    report = json.loads(subprocess.check_output([str(FFPROBE), '-v', 'error', '-show_streams', '-of', 'json', str(path)], encoding='utf-8'))
    video = next(s for s in report['streams'] if s['codec_type'] == 'video')
    results.append({'file': name, 'codec': video['codec_name'], 'pixel_format': video['pix_fmt'], 'profile': video.get('profile'), 'raw_bits': video.get('bits_per_raw_sample')})
    print(name, video['codec_name'], video['pix_fmt'], flush=True)
(DEST / 'corrupto.mp4').write_bytes(b'This is deliberately not a video file.')
(DEST / 'subtitulos.srt').write_text('1\n00:00:00,000 --> 00:00:02,000\nPrueba de subtítulos: áéíóú ñ\n', encoding='utf-8')
(DEST / 'manifest.json').write_text(json.dumps(results, indent=2, ensure_ascii=False), encoding='utf-8')
subprocess.run([str(FFMPEG), '-y', '-hide_banner', '-loglevel', 'error', '-f', 'lavfi', '-i', 'testsrc2=size=3840x2160:rate=24:duration=6', '-c:v', 'libx265', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p10le', '-x265-params', 'log-level=error:pools=4', '-an', str(DEST / 'h265-4K-10bit.mp4')], check=True)
